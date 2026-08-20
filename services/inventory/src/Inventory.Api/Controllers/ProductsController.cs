using Inventory.Api.Application;
using Inventory.Api.Contracts;
using Inventory.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IReadOnlyProductCatalog products) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProductPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductPageResponse>> Search(
        [FromQuery] string? query = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await products.SearchAsync(query, page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(id, cancellationToken);
        if (product is null)
        {
            throw InventoryApiException.NotFound(
                ErrorCodes.ProductNotFound,
                $"Product '{id}' was not found.");
        }

        return Ok(product);
    }
}
