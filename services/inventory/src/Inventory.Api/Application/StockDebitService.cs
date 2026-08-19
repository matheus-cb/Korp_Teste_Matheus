using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Contracts;
using Inventory.Api.Domain;
using Inventory.Api.Errors;
using Inventory.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Application;

public interface IStockDebitService
{
    Task<StockDebitOperationResponse> ExecuteAsync(
        Guid attemptId,
        StockDebitRequest request,
        CancellationToken cancellationToken);
    Task<StockDebitOperationResponse?> GetAsync(Guid attemptId, CancellationToken cancellationToken);
}

public sealed class StockDebitService(
    InventoryDbContext database,
    TimeProvider timeProvider) : IStockDebitService
{
    public async Task<StockDebitOperationResponse> ExecuteAsync(
        Guid attemptId,
        StockDebitRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(attemptId, request);
        var requestHash = StockDebitPayloadHasher.Compute(request);

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // This transaction-scoped PostgreSQL advisory lock serializes requests using
        // the same idempotency key, including the first insert of that key.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({attemptId.ToString("N")}, 0))",
            cancellationToken);

        var existing = await database.StockDebitOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.AttemptId == attemptId, cancellationToken);

        if (existing is not null)
        {
            EnsureSamePayload(existing, requestHash);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(existing);
        }

        var now = timeProvider.GetUtcNow();
        var operation = StockDebitOperation.Start(attemptId, request.InvoiceId, requestHash, now);
        database.StockDebitOperations.Add(operation);

        var lockedProducts = new Dictionary<Guid, Product>();
        foreach (var item in request.Items.OrderBy(item => item.ProductId))
        {
            var product = await database.Products
                .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {item.ProductId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                operation.Reject(
                    ErrorCodes.ProductNotFound,
                    $"Product '{item.ProductId}' was not found.",
                    timeProvider.GetUtcNow());
                await SaveAndCommitAsync(transaction, cancellationToken);
                return ToResponse(operation);
            }

            lockedProducts.Add(product.Id, product);
        }

        // Produto sem controle de estoque entra na nota sem validar saldo.
        var insufficient = request.Items
            .FirstOrDefault(item => !lockedProducts[item.ProductId].CanFulfill(item.Quantity));
        if (insufficient is not null)
        {
            var product = lockedProducts[insufficient.ProductId];
            operation.Reject(
                ErrorCodes.InsufficientStock,
                $"Saldo insuficiente para {product.Code}: disponivel {product.Balance}, solicitado {insufficient.Quantity}.",
                timeProvider.GetUtcNow());
            await SaveAndCommitAsync(transaction, cancellationToken);
            return ToResponse(operation);
        }

        // INV-04: item que nao movimenta e reportado, nunca ignorado em silencio.
        var ignored = new List<IgnoredStockItem>();

        foreach (var item in request.Items)
        {
            var product = lockedProducts[item.ProductId];
            if (!product.TracksStock)
            {
                // Sem controle: nenhum saldo muda e nenhum movimento e gerado,
                // senao a auditoria mostraria uma baixa que nao existiu.
                ignored.Add(new IgnoredStockItem(
                    product.Id,
                    product.Code,
                    item.Quantity,
                    "PRODUCT_DOES_NOT_TRACK_STOCK",
                    "Produto nao controla estoque; o item entrou na nota sem movimentar saldo."));
                continue;
            }

            var balanceBefore = product.Balance;
            product.Debit(item.Quantity);
            operation.Movements.Add(StockMovement.Create(
                operation.Id,
                product.Id,
                item.Quantity,
                balanceBefore,
                timeProvider.GetUtcNow()));
        }

        if (ignored.Count > 0)
        {
            operation.RecordIgnoredItems(JsonSerializer.Serialize(ignored, IgnoredItemsJson));
        }

        operation.Complete(timeProvider.GetUtcNow());
        await SaveAndCommitAsync(transaction, cancellationToken);
        return ToResponse(operation);
    }

    public async Task<StockDebitOperationResponse?> GetAsync(
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var operation = await database.StockDebitOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.AttemptId == attemptId, cancellationToken);
        return operation is null ? null : ToResponse(operation);
    }

    private async Task SaveAndCommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureSamePayload(StockDebitOperation existing, string requestHash)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(existing.RequestHash),
                Encoding.ASCII.GetBytes(requestHash)))
        {
            throw InventoryApiException.Conflict(
                ErrorCodes.IdempotencyKeyReused,
                "The idempotency key has already been used with a different payload.");
        }
    }

    private static void ValidateRequest(Guid attemptId, StockDebitRequest request)
    {
        if (attemptId == Guid.Empty)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.IdempotencyKeyInvalid,
                "Idempotency-Key must be a non-empty UUID.");
        }

        if (request.InvoiceId == Guid.Empty)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "invoiceId must be a non-empty UUID.");
        }

        if (request.Items is null || request.Items.Count is < 1 or > 100)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "A stock debit requires between 1 and 100 items.");
        }

        if (request.Items.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0))
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "Each stock debit item requires a productId and a positive quantity.");
        }

        if (request.Items.Select(item => item.ProductId).Distinct().Count() != request.Items.Count)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "A product can appear only once in a stock debit.");
        }
    }

    private static readonly JsonSerializerOptions IgnoredItemsJson =
        new(JsonSerializerDefaults.Web);

    private static List<IgnoredStockItem> ReadIgnoredItems(StockDebitOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.IgnoredItemsJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<IgnoredStockItem>>(
            operation.IgnoredItemsJson,
            IgnoredItemsJson) ?? [];
    }

    private static StockDebitOperationResponse ToResponse(StockDebitOperation operation) =>
        new(
            operation.Id,
            operation.State.ToString(),
            operation.ErrorCode,
            operation.ErrorMessage,
            ReadIgnoredItems(operation));
}

public static class StockDebitPayloadHasher
{
    public static string Compute(StockDebitRequest request)
    {
        var canonicalPayload = new StringBuilder(request.InvoiceId.ToString("N"));
        foreach (var item in request.Items.OrderBy(item => item.ProductId))
        {
            canonicalPayload
                .Append('|')
                .Append(item.ProductId.ToString("N"))
                .Append(':')
                .Append(item.Quantity);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload.ToString())));
    }
}
