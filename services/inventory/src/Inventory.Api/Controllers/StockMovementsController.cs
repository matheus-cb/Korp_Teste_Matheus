using Inventory.Api.Application;
using Inventory.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// Extrato de movimentação (UC-09). É consulta de leitura, como o catálogo, e
/// por isso vive fora de `/api/stock`, que é reservado a operação interna.
/// A consulta em si vive em <see cref="IStockMovementReader"/>, compartilhada
/// com a tool MCP <c>list_movements</c>.
/// </summary>
[ApiController]
[Route("api/movements")]
public sealed class StockMovementsController(IStockMovementReader reader) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StockMovementPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StockMovementPageResponse>> Search(
        [FromQuery] Guid? productId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await reader.SearchAsync(productId, page, pageSize, cancellationToken));
    }
}
