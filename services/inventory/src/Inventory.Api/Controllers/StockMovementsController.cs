using Inventory.Api.Contracts;
using Inventory.Api.Domain;
using Inventory.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// Extrato de movimentação (UC-09). É consulta de leitura, como o catálogo, e
/// por isso vive fora de `/api/stock`, que é reservado a operação interna.
/// </summary>
[ApiController]
[Route("api/movements")]
public sealed class StockMovementsController(InventoryDbContext database) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StockMovementPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StockMovementPageResponse>> Search(
        [FromQuery] Guid? productId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
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

        return Ok(new StockMovementPageResponse(items, page, pageSize, total));
    }
}
