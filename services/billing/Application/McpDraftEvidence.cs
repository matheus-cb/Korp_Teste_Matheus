using System.Text.Json;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;

namespace Billing.Api.Application;

/// <summary>
/// Evidência colhida das ferramentas MCP durante uma execução, e o juiz do que
/// o modelo pode afirmar com base nela.
///
/// <para>
/// Esta classe existe por causa da seção 2.2 do <c>docs/plano-agente-vps.md</c>:
/// <c>DiscoveredProductIds</c> faz parte do contrato de retorno e é preenchido
/// pelo provedor, então a defesa vale o quanto o provedor for honesto. Enquanto
/// cada cliente tinha sua própria cópia da lógica, "ser honesto" dependia de
/// quem escrevesse o próximo provedor lembrar de copiar direito — e uma
/// correção podia entrar num cliente e não no outro.
/// </para>
///
/// <para>
/// Aqui o provedor não declara nada: ele entrega resultado de ferramenta, e o
/// conjunto de descobertos é <b>derivado</b> disso. Um provedor "generoso", que
/// tente afirmar um produto que nenhuma ferramenta devolveu, é reprovado em
/// <see cref="BuildItems"/> — comportamento coberto por teste.
/// </para>
/// </summary>
public sealed class McpDraftEvidence
{
    private readonly Dictionary<Guid, DiscoveredProduct> catalog = [];
    private readonly Dictionary<AvailabilityKey, AvailabilityProof> proofs = [];

    /// <summary>Teto de itens do rascunho final, igual para qualquer provedor.</summary>
    public const int MaxItems = 20;

    /// <summary>Produtos vistos em resultado de ferramenta, e só eles.</summary>
    public IReadOnlySet<Guid> DiscoveredProductIds => catalog.Keys.ToHashSet();

    public bool Knows(Guid productId) => catalog.ContainsKey(productId);

    /// <summary>
    /// Recolhe o que um resultado MCP prova. Chame só com resultado de ferramenta
    /// do Inventory: ferramenta local responde sobre nota, e nota não é fonte de
    /// produto válido para um rascunho novo.
    /// </summary>
    public void Capture(string toolName, JsonElement arguments, AiToolResult result)
    {
        EnsureSucceeded(toolName, result);

        if (toolName == "check_availability")
            CaptureProofs(arguments, result.Content);

        CaptureProducts(result.Content);
    }

    /// <summary>
    /// Valida o que o modelo propôs contra a evidência e monta os itens finais.
    /// Código e descrição saem do catálogo, não do que o modelo disse.
    /// </summary>
    public IReadOnlyList<AiDraftModelItem> BuildItems(IReadOnlyList<(Guid ProductId, int Quantity)> proposed)
    {
        var parsed = new List<(Guid Id, DiscoveredProduct Product, int Quantity)>();
        foreach (var (productId, quantity) in proposed)
        {
            // A defesa central: o id precisa ter vindo de um resultado MCP.
            if (!catalog.TryGetValue(productId, out var product))
                throw new InvalidOperationException("The model returned a product that was not discovered through MCP.");
            if (quantity <= 0)
                throw new InvalidOperationException("The model returned a non-positive quantity.");

            parsed.Add((productId, product, quantity));
        }

        if (parsed.Count > MaxItems)
            throw new InvalidOperationException("The draft exceeds the maximum number of items.");

        var totals = parsed
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => checked(group.Sum(item => item.Quantity)));

        var availability = new Dictionary<Guid, string>();
        foreach (var (productId, quantity) in totals)
        {
            // Disponibilidade vem da prova do servidor, nunca do que o modelo
            // alegou. Sem prova para a quantidade agregada, reprova.
            if (!proofs.TryGetValue(new AvailabilityKey(productId, quantity), out var proof))
            {
                throw new InvalidOperationException(
                    "Every final product and aggregate quantity requires a successful MCP availability proof.");
            }

            availability[productId] = proof.Exists
                ? proof.AvailableBalance >= quantity ? "available" : "insufficient"
                : "unknown";
        }

        return parsed
            .Select(item => new AiDraftModelItem(
                item.Id,
                item.Product.Code,
                item.Product.Description,
                item.Quantity,
                availability[item.Id]))
            .ToList();
    }

    /// <summary>
    /// Restrições por ferramenta, antes da chamada. Ficam aqui, e não no cliente,
    /// para valerem para qualquer provedor (seção 2.3 do plano).
    /// </summary>
    public void ValidateCall(string toolName, JsonElement arguments)
    {
        switch (toolName)
        {
            case "search_products":
            case "list_products":
            case "list_movements":
                break;

            case "get_product":
                // Sem isto o modelo pode sondar o catálogo por id.
                var raw = arguments.ValueKind == JsonValueKind.Object &&
                          arguments.TryGetProperty("productId", out var value)
                    ? value.GetString()
                    : null;
                if (!Guid.TryParse(raw, out var id) || !catalog.ContainsKey(id))
                    throw new InvalidOperationException("get_product requires a product id already discovered.");
                break;

            case "check_availability":
                if (arguments.ValueKind != JsonValueKind.Object ||
                    !arguments.TryGetProperty("items", out var items) ||
                    items.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("check_availability requires an items array.");
                }

                var count = items.GetArrayLength();
                if (count is 0 or > MaxItems)
                    throw new InvalidOperationException("check_availability item count is out of range.");
                break;

            default:
                throw new InvalidOperationException($"Tool '{toolName}' is not allow-listed.");
        }
    }

    private static void EnsureSucceeded(string toolName, AiToolResult result)
    {
        if (result.IsError)
            throw new InvalidOperationException($"MCP tool '{toolName}' returned an error.");
        if (result.Content.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"MCP tool '{toolName}' returned an invalid result.");
        if (result.Content.TryGetProperty("errorCode", out var code) && code.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException($"MCP tool '{toolName}' returned a semantic error.");
    }

    private void CaptureProducts(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            Guid id = default;
            string? code = null;
            string? description = null;

            foreach (var property in element.EnumerateObject())
            {
                if ((property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Equals("productId", StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    if (!Guid.TryParse(property.Value.GetString(), out id)) id = Guid.Empty;
                }
                else if (property.Name.Equals("code", StringComparison.OrdinalIgnoreCase))
                {
                    code = property.Value.GetString();
                }
                else if (property.Name.Equals("description", StringComparison.OrdinalIgnoreCase))
                {
                    description = property.Value.GetString();
                }

                CaptureProducts(property.Value);
            }

            if (id != Guid.Empty && !string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(description))
                catalog[id] = new DiscoveredProduct(id, code, description);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CaptureProducts(child);
        }
    }

    private void CaptureProofs(JsonElement arguments, JsonElement content)
    {
        var requested = arguments.GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                item => Guid.Parse(item.GetProperty("productId").GetString()!),
                item => item.GetProperty("quantity").GetInt32());

        if (!content.TryGetProperty("items", out var returned) ||
            returned.ValueKind != JsonValueKind.Array ||
            returned.GetArrayLength() != requested.Count)
        {
            throw new InvalidOperationException("MCP availability returned an incomplete result.");
        }

        foreach (var item in returned.EnumerateArray())
        {
            if (!Guid.TryParse(item.GetProperty("productId").GetString(), out var productId) ||
                !requested.TryGetValue(productId, out var expected))
            {
                throw new InvalidOperationException("MCP availability returned an unexpected product.");
            }

            var quantity = item.GetProperty("requestedQuantity").GetInt32();
            var balance = item.GetProperty("availableBalance").GetInt32();
            var exists = item.GetProperty("exists").GetBoolean();
            var isAvailable = item.GetProperty("isAvailable").GetBoolean();

            // Coerência interna: o servidor não pode se contradizer.
            if (quantity != expected || balance < 0 || isAvailable != (exists && balance >= quantity))
                throw new InvalidOperationException("MCP availability returned inconsistent stock evidence.");

            proofs[new AvailabilityKey(productId, quantity)] = new AvailabilityProof(balance, exists);
        }
    }

    private readonly record struct AvailabilityKey(Guid ProductId, int RequestedQuantity);

    private sealed record DiscoveredProduct(Guid Id, string Code, string Description);

    private sealed record AvailabilityProof(int AvailableBalance, bool Exists);
}
