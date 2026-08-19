using System.Text.Json;
using Billing.Api.Api;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Application;

public sealed class ClosureCoordinator(
    IDbContextFactory<BillingDbContext> dbFactory,
    IInventoryClient inventory,
    TimeProvider clock,
    IHttpContextAccessor httpContext,
    ILogger<ClosureCoordinator> logger)
{
    public async Task<ClosureResult> ProcessAsync(Guid attemptId, bool sendDebitWhenUnknown, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var attempt = await db.ClosureAttempts
            .SingleOrDefaultAsync(x => x.Id == attemptId, cancellationToken)
            ?? throw new ResourceNotFoundException("CLOSURE_ATTEMPT_NOT_FOUND", "Tentativa de fechamento não encontrada.");
        var invoice = await db.Invoices
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == attempt.InvoiceId, cancellationToken);

        if (attempt.State != ClosureAttemptState.Pending)
            return new(attempt.State, attempt.ErrorCode, attempt.ErrorMessage);

        StockDebitOutcome outcome;
        try
        {
            if (sendDebitWhenUnknown)
            {
                outcome = await inventory.DebitAsync(
                    attempt.Id,
                    attempt.InvoiceId,
                    invoice.Items.Select(x => new StockDebitItem(x.ProductId, x.Quantity)).ToList(),
                    cancellationToken);
            }
            else
            {
                outcome = await inventory.GetDebitAsync(attempt.Id, cancellationToken);
                if (outcome.NotFound)
                {
                    outcome = await inventory.DebitAsync(
                        attempt.Id,
                        attempt.InvoiceId,
                        invoice.Items.Select(x => new StockDebitItem(x.ProductId, x.Quantity)).ToList(),
                        cancellationToken);
                }
            }
        }
        catch (DependencyUnavailableException ex)
        {
            attempt.RecordTransientFailure(ex.Code, ex.Message, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Closure attempt {AttemptId} remains pending after inventory failure", attempt.Id);
            return new(ClosureAttemptState.Pending, ex.Code, ex.Message);
        }

        var now = clock.GetUtcNow();
        if (outcome.IsCompleted)
        {
            if (outcome.IgnoredItems is { Count: > 0 })
            {
                // INV-04: itens que nao movimentaram viajam com a tentativa,
                // para a repeticao idempotente devolver a mesma informacao.
                attempt.RecordIgnoredItems(JsonSerializer.Serialize(outcome.IgnoredItems));
            }

            attempt.Complete(now);
            invoice.Close(now, httpContext.ActingUserName());
        }
        else if (outcome.IsRejected)
        {
            attempt.Reject(
                outcome.ErrorCode ?? "INVENTORY_REJECTED",
                outcome.ErrorMessage ?? "O estoque rejeitou a baixa.",
                now);
        }
        else
        {
            attempt.RecordTransientFailure("CLOSURE_PENDING", "O fechamento ainda está sendo confirmado.", now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Closure attempt {AttemptId} was reconciled concurrently", attempt.Id);
            db.ChangeTracker.Clear();
            var reconciled = await db.ClosureAttempts.AsNoTracking()
                .SingleAsync(x => x.Id == attempt.Id, cancellationToken);
            return new(reconciled.State, reconciled.ErrorCode, reconciled.ErrorMessage);
        }

        return new(attempt.State, attempt.ErrorCode, attempt.ErrorMessage);
    }
}

public sealed record ClosureResult(ClosureAttemptState State, string? ErrorCode, string? ErrorMessage);

public sealed class ClosureReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<ClosureReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var ids = await db.ClosureAttempts.AsNoTracking()
                    .Where(x => x.State == ClosureAttemptState.Pending && x.NextRetryAt <= clock.GetUtcNow())
                    .OrderBy(x => x.NextRetryAt)
                    .Select(x => x.Id)
                    .Take(10)
                    .ToListAsync(stoppingToken);
                var coordinator = scope.ServiceProvider.GetRequiredService<ClosureCoordinator>();
                foreach (var id in ids)
                {
                    try
                    {
                        await coordinator.ProcessAsync(id, false, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Closure attempt {AttemptId} reconciliation failed unexpectedly", id);
                        await RecordUnexpectedFailureAsync(id, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Closure reconciliation iteration failed");
            }
        }
    }

    private async Task RecordUnexpectedFailureAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var attempt = await db.ClosureAttempts.SingleOrDefaultAsync(
                x => x.Id == attemptId && x.State == ClosureAttemptState.Pending,
                cancellationToken);
            if (attempt is null) return;

            attempt.RecordTransientFailure(
                "RECONCILIATION_FAILED",
                "Não foi possível confirmar o fechamento nesta tentativa.",
                clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not postpone closure attempt {AttemptId} after reconciliation failure", attemptId);
        }
    }
}
