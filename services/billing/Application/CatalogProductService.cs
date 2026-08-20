using Billing.Api.Api;
using Billing.Api.Infrastructure;

namespace Billing.Api.Application;

/// <summary>Fachada autenticada: identidade humana é resolvida no Billing antes do comando interno.</summary>
public sealed class CatalogProductService(IInventoryClient inventory, IHttpContextAccessor httpContext)
{
    public Task<InventoryProduct> CreateAsync(string code, string description, int balance, bool tracksStock, CancellationToken cancellationToken) =>
        inventory.CreateProductAsync(code, description, balance, tracksStock, httpContext.ActingUserName(), cancellationToken);

    public Task<InventoryProduct> UpdateAsync(Guid id, string code, string description, bool tracksStock, Guid version, CancellationToken cancellationToken) =>
        inventory.UpdateProductAsync(id, code, description, tracksStock, version, httpContext.ActingUserName(), cancellationToken);
}
