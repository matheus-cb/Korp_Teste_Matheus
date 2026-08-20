using Inventory.Api.Contracts;
using Inventory.Api.Domain;
using Inventory.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Application;

/// <summary>
/// Leitura do extrato de movimentação. Extraída do controller para que a tool
/// MCP e a rota HTTP respondam pela mesma consulta — duas cópias divergiriam, e
/// a regra de só considerar operações concluídas é justamente o tipo de detalhe
/// que se perde numa cópia.
/// </summary>
public interface IStockMovementReader
{
    Task<StockMovementPageResponse> SearchAsync(
        Guid? productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed class StockMovementReader(InventoryDbContext database) : IStockMovementReader
{
    public async Task<StockMovementPageResponse> SearchAsync(
        Guid? productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        // Só movimentos de operações concluídas são fato consumado; pendentes e
        // rejeitados não alteraram saldo e confundiriam o extrato.
        var query = database.StockMovements
            .AsNoTracking()
            .Where(movement => movement.Operation!.State == StockDebitState.Completed);

        if (productId is { } id)
        {
            query = query.Where(movement => movement.ProductId == id);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(movement => movement.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(movement => new StockMovementResponse(
                movement.Id,
                movement.ProductId,
                movement.Product!.Code,
                movement.Product.Description,
                movement.Quantity,
                movement.BalanceBefore,
                movement.BalanceAfter,
                movement.Operation!.InvoiceId,
                movement.CreatedAt))
            .ToListAsync(cancellationToken);

        return new StockMovementPageResponse(items, page, pageSize, total);
    }
}
