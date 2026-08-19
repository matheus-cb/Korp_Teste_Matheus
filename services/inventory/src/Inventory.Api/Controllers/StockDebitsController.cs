using Inventory.Api.Application;
using Inventory.Api.Contracts;
using Inventory.Api.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/stock/debits")]
public sealed class StockDebitsController(IStockDebitService stockDebits) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<StockDebitOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<StockDebitOperationResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockDebitOperationResponse>> Debit(
        [FromBody] StockDebitRequest request,
        CancellationToken cancellationToken)
    {
        var attemptId = ReadIdempotencyKey(Request.Headers["Idempotency-Key"]);
        var result = await stockDebits.ExecuteAsync(attemptId, request, cancellationToken);

        return string.Equals(result.State, "Rejected", StringComparison.Ordinal)
            ? Conflict(result)
            : Ok(result);
    }

    [HttpGet("{attemptId:guid}")]
    [ProducesResponseType<StockDebitOperationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockDebitOperationResponse>> Get(
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var result = await stockDebits.GetAsync(attemptId, cancellationToken);
        if (result is null)
        {
            throw InventoryApiException.NotFound(
                ErrorCodes.StockDebitNotFound,
                $"Stock debit attempt '{attemptId}' was not found.");
        }

        return Ok(result);
    }

    private static Guid ReadIdempotencyKey(StringValues values)
    {
        if (StringValues.IsNullOrEmpty(values))
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.IdempotencyKeyRequired,
                "The Idempotency-Key header is required.");
        }

        var value = values.ToString();
        if (values.Count != 1 || !Guid.TryParse(value, out var attemptId) || attemptId == Guid.Empty)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.IdempotencyKeyInvalid,
                "Idempotency-Key must contain one non-empty UUID.");
        }

        return attemptId;
    }
}
