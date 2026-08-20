using Billing.Api.Application;

namespace Billing.Api.Api;

public sealed record CatalogProductRequest(string Code, string Description, int Balance, bool TracksStock = true);
public sealed record CatalogProductUpdateRequest(string Code, string Description, bool TracksStock);

public static class CatalogProductEndpoints
{
    public static IEndpointRouteBuilder MapCatalogProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/catalog/products").WithTags("Catalog commands");
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CatalogProductRequest request, CatalogProductService service, CancellationToken cancellationToken) =>
        Results.Created("/api/catalog/products", await service.CreateAsync(request.Code, request.Description, request.Balance, request.TracksStock, cancellationToken));

    private static async Task<IResult> UpdateAsync(Guid id, CatalogProductUpdateRequest request, HttpRequest httpRequest, CatalogProductService service, CancellationToken cancellationToken)
    {
        var raw = httpRequest.Headers["If-Match"].FirstOrDefault()?.Trim().Trim('"');
        if (!Guid.TryParse(raw, out var version))
            throw new Domain.ConflictException("PRODUCT_VERSION_REQUIRED", "Atualize o produto antes de salvar para evitar sobrescrever alterações.");
        return Results.Ok(await service.UpdateAsync(id, request.Code, request.Description, request.TracksStock, version, cancellationToken));
    }
}
