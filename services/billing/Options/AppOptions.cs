namespace Billing.Api.Options;

public sealed class InventoryOptions
{
    public const string Section = "Inventory";
    public required string BaseUrl { get; init; }
    public required string McpEndpoint { get; init; }
    public int TimeoutSeconds { get; init; } = 5;
}

public sealed class OpenAiOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string Model { get; init; } = "gpt-5.6-luna";
    public string PromptVersion { get; init; } = "invoice-draft-v1";
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxOutputTokens { get; init; } = 1500;
    public int MaxToolCalls { get; init; } = 8;
    public decimal EstimatedUsdPerMillionInputTokens { get; init; }
    public decimal EstimatedUsdPerMillionOutputTokens { get; init; }
}

/// <summary>
/// Ponte de inferencia local (Claude Code na VPS). Ver ClaudeBridgeClient:
/// a ponte so faz inferencia; MCP e validacao continuam no Billing.
/// </summary>
public sealed class ClaudeBridgeOptions
{
    public const string Section = "ClaudeBridge";
    public string BaseUrl { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
    public string Model { get; init; } = "sonnet";
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed class InternalAuthOptions
{
    public const string Section = "InternalAuth";
    public string Token { get; init; } = string.Empty;
    public bool AllowUnauthenticated { get; init; }
}
