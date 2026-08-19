using Inventory.Api.Application;
using Inventory.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// Reconciliação de saldo (INV-09 / UC-11). Rota interna: é diagnóstico de
/// operação, não consulta pública de catálogo, e por isso fica fora do edge.
/// </summary>
[ApiController]
[Route("api/stock/reconciliation")]
public sealed class StockReconciliationController(IStockReconciliation reconciliation) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<StockReconciliationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StockReconciliationResponse>> Run(CancellationToken cancellationToken)
    {
        var result = await reconciliation.RunAsync(cancellationToken);

        // Divergência não é erro de requisição: é achado. O chamador decide.
        return Ok(result);
    }
}
