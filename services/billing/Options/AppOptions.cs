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

public sealed class InternalAuthOptions
{
    public const string Section = "InternalAuth";
    public string Token { get; init; } = string.Empty;
    public bool AllowUnauthenticated { get; init; }
}
