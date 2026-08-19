using Inventory.Api.Contracts;
using Inventory.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Application;

/// <summary>
/// INV-09: o saldo é uma projeção verificável do extrato, não um número solto.
///
/// Para qualquer produto controlado deve valer
/// <c>saldo == saldoInicial − Σ(baixas)</c>. Como o extrato guarda
/// <c>BalanceBefore</c> e <c>BalanceAfter</c> de cada movimento, dá para
/// reconstituir a cadeia e apontar exatamente onde ela quebra.
///
/// O legado que serviu de referência tem uma função manual de recálculo — sinal
/// de que admite divergência. Aqui a reconciliação existe para provar que não
/// há, e para achar rápido se houver.
/// </summary>
public interface IStockReconciliation
{
    Task<StockReconciliationResponse> RunAsync(CancellationToken cancellationToken);
}

public sealed class StockReconciliation(InventoryDbContext database, TimeProvider timeProvider)
    : IStockReconciliation
{
    public async Task<StockReconciliationResponse> RunAsync(CancellationToken cancellationToken)
    {
        // Só movimentos de operações concluídas contam para o saldo.
        var debits = await database.StockMovements
            .AsNoTracking()
            .Where(movement => movement.Operation!.State == Domain.StockDebitState.Completed)
            .GroupBy(movement => movement.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                TotalDebited = group.Sum(movement => movement.Quantity),
                Movements = group.Count(),
            })
            .ToDictionaryAsync(entry => entry.ProductId, cancellationToken);

        var products = await database.Products
            .AsNoTracking()
            .Where(product => product.TracksStock)
            .OrderBy(product => product.Code)
            .ToListAsync(cancellationToken);

        var divergences = new List<StockDivergence>();
        foreach (var product in products)
        {
            debits.TryGetValue(product.Id, out var debit);
            var totalDebited = debit?.TotalDebited ?? 0;

            // O saldo inicial não é gravado; reconstituímos pelo primeiro
            // movimento (BalanceBefore) ou, sem movimento, pelo saldo atual.
            var openingBalance = await database.StockMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.ProductId == product.Id &&
                    movement.Operation!.State == Domain.StockDebitState.Completed)
                .OrderBy(movement => movement.CreatedAt)
                .Select(movement => (int?)movement.BalanceBefore)
                .FirstOrDefaultAsync(cancellationToken) ?? product.Balance;

            var expected = openingBalance - totalDebited;
            if (expected != product.Balance)
            {
                divergences.Add(new StockDivergence(
                    product.Id,
                    product.Code,
                    product.Description,
                    product.Balance,
                    expected,
                    product.Balance - expected,
                    debit?.Movements ?? 0));
            }
        }

        return new StockReconciliationResponse(
            timeProvider.GetUtcNow(),
            products.Count,
            divergences.Count == 0,
            divergences);
    }
}
