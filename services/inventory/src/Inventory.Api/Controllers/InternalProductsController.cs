using Inventory.Api.Application;
using Inventory.Api.Contracts;
using Inventory.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>Comandos chegam apenas pelo Billing autenticado; o navegador não escolhe o ator.</summary>
[ApiController]
[Route("api/internal/products")]
public sealed class InternalProductsController(IProductCatalog products) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await products.CreateAsync(request, ActorName(), cancellationToken);
        return Created($"/api/products/{product.Id}", product);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var version = Request.Headers["If-Match"].FirstOrDefault()?.Trim().Trim('"');
        if (!Guid.TryParse(version, out var expectedVersion))
            throw InventoryApiException.Conflict("PRODUCT_VERSION_REQUIRED", "Atualize o produto antes de salvar para evitar sobrescrever alterações.");
        return Ok(await products.UpdateAsync(id, request, expectedVersion, ActorName(), cancellationToken));
    }

    private string ActorName()
    {
        var actor = Request.Headers["X-Notaflow-Actor"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 120)
            throw InventoryApiException.BadRequest(ErrorCodes.ValidationError, "Ator interno obrigatório.");
        return actor.Trim();
    }
}
