namespace Billing.Api.Contracts;

public sealed record AiDraftResponse(
    Guid RunId,
    IReadOnlyList<AiDraftItem> Items,
    IReadOnlyList<UnresolvedDraftItem> UnresolvedItems,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AiDraftStep> Steps);

public sealed record AiDraftItem(
    Guid ProductId,
    string Code,
    string Description,
    int Quantity,
    string Availability);

public sealed record UnresolvedDraftItem(string Description, int? Quantity, string Reason);
public sealed record AiDraftStep(string Tool, string Summary, string Status);

public sealed record AiDraftModelResult(
    IReadOnlyList<AiDraftModelItem> Items,
    IReadOnlyList<UnresolvedDraftItem> UnresolvedItems,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AiDraftStep> Steps,
    IReadOnlySet<Guid> DiscoveredProductIds,
    IReadOnlyList<string> ToolNames,
    int InputTokens,
    int OutputTokens);

public sealed record AiDraftModelItem(
    Guid ProductId,
    string Code,
    string Description,
    int Quantity,
    string Availability);

public sealed record AiDraftInput(string? Text, byte[]? ImageBytes, string? ImageMediaType);
