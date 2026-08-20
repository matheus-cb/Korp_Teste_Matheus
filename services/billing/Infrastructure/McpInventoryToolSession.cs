using System.Text.Json;
using Billing.Api.Options;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Billing.Api.Infrastructure;

public sealed record AiToolDefinition(string Name, string Description, JsonElement InputSchema);
public sealed record AiToolResult(JsonElement Content, bool IsError);

public interface IInventoryToolSession : IAsyncDisposable
{
    IReadOnlyList<AiToolDefinition> Tools { get; }
    Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken);
}

public interface IInventoryToolSessionFactory
{
    Task<IInventoryToolSession> OpenAsync(CancellationToken cancellationToken);
}

public sealed class McpInventoryToolSessionFactory(
    IConfiguration configuration,
    IOptions<InternalAuthOptions> internalAuth,
    IHttpContextAccessor httpContextAccessor,
    ILoggerFactory loggerFactory) : IInventoryToolSessionFactory
{
    // Allowlist do cliente MCP. Todas somente leitura (INV-27): o servidor
    // declara ReadOnly/OpenWorld e a checagem abaixo recusa a sessao se alguma
    // deixar de ser. Criar ou fechar nota nunca entra aqui -- e dominio do
    // proprio Billing, que e o cliente, nao o servidor MCP.
    private static readonly HashSet<string> AllowedTools =
    [
        "search_products",
        "get_product",
        "check_availability",
        "list_products",
        "list_movements"
    ];

    public async Task<IInventoryToolSession> OpenAsync(CancellationToken cancellationToken)
    {
        var endpoint = configuration["Inventory:McpEndpoint"]
            ?? throw new InvalidOperationException("Inventory:McpEndpoint is required.");
        var headers = InternalRequestHeaders.Build(internalAuth.Value.Token, httpContextAccessor.HttpContext);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(configuration.GetValue("Inventory:TimeoutSeconds", 5)),
                EnableStandaloneGetStream = false,
                Name = "notaflow-billing",
                AdditionalHeaders = headers
            },
            loggerFactory: loggerFactory);
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var discovered = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var selected = discovered
            .Where(x => AllowedTools.Contains(x.Name) &&
                        x.ProtocolTool.Annotations?.ReadOnlyHint == true &&
                        x.ProtocolTool.Annotations.OpenWorldHint == false)
            .ToDictionary(x => x.Name, StringComparer.Ordinal);

        if (selected.Count != AllowedTools.Count)
        {
            await client.DisposeAsync();
            throw new InvalidOperationException("The inventory MCP server did not expose the required read-only tools.");
        }

        var tools = selected.Values.Select(x => new AiToolDefinition(
            x.Name,
            x.Description,
            x.ProtocolTool.InputSchema.Clone())).ToList();
        return new McpInventoryToolSession(client, selected, tools);
    }

    private sealed class McpInventoryToolSession(
        McpClient client,
        IReadOnlyDictionary<string, McpClientTool> toolsByName,
        IReadOnlyList<AiToolDefinition> tools) : IInventoryToolSession
    {
        public IReadOnlyList<AiToolDefinition> Tools { get; } = tools;

        public async Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
        {
            if (!toolsByName.ContainsKey(name))
                throw new InvalidOperationException("The requested MCP tool is not allow-listed.");
            if (arguments.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("MCP tool arguments must be a JSON object.");

            var dictionary = arguments.Deserialize<Dictionary<string, object?>>() ?? [];
            var result = await client.CallToolAsync(name, dictionary, cancellationToken: cancellationToken);
            if (result.StructuredContent is { } structured)
                return new(structured.Clone(), result.IsError == true);

            var text = result.Content.OfType<TextContentBlock>().Select(x => x.Text).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(text))
                return new(JsonSerializer.SerializeToElement(new { }), result.IsError == true);
            using var document = JsonDocument.Parse(text);
            return new(document.RootElement.Clone(), result.IsError == true);
        }

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
