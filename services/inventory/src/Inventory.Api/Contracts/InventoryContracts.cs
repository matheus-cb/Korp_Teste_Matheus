using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Contracts;

public sealed record CreateProductRequest(
    [Required, StringLength(64, MinimumLength = 1)] string Code,
    [Required, StringLength(200, MinimumLength = 1)] string Description,
    [Range(0, int.MaxValue)] int Balance,
    bool TracksStock = true);

public sealed record UpdateProductRequest(
    [Required, StringLength(64, MinimumLength = 1)] string Code,
    [Required, StringLength(200, MinimumLength = 1)] string Description,
    bool TracksStock);

public sealed record ProductResponse(
    Guid Id,
    string Code,
    string Description,
    int Balance,
    bool TracksStock,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    Guid Version);

public sealed record ProductPageResponse(
    IReadOnlyList<ProductResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record StockDebitRequest(
    [Required] Guid InvoiceId,
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<StockDebitItemRequest> Items);

public sealed record StockDebitItemRequest(
    [Required] Guid ProductId,
    [Range(1, int.MaxValue)] int Quantity);

public sealed record StockDebitOperationResponse(
    Guid OperationId,
    string State,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<IgnoredStockItem>? IgnoredItems = null);

/// <summary>
/// Item que entrou na nota sem movimentar estoque. INV-04: reportar, nunca
/// ignorar em silencio.
/// </summary>
public sealed record IgnoredStockItem(
    Guid ProductId,
    string Code,
    int Quantity,
    string Reason,
    string Message);

public sealed record AvailabilityRequest(
    [Required, MinLength(1), MaxLength(20)] IReadOnlyList<AvailabilityItemRequest> Items);

public sealed record AvailabilityItemRequest(Guid ProductId, int Quantity);

public sealed record ProductAvailabilityResponse(
    Guid ProductId,
    string? Code,
    string? Description,
    int RequestedQuantity,
    int AvailableBalance,
    bool Exists,
    bool TracksStock,
    bool IsAvailable);

/// <summary>Resultado da reconciliacao de saldo (INV-09).</summary>
public sealed record StockReconciliationResponse(
    DateTimeOffset RunAt,
    int ProductsChecked,
    bool IsConsistent,
    IReadOnlyList<StockDivergence> Divergences);

public sealed record StockDivergence(
    Guid ProductId,
    string Code,
    string Description,
    int CurrentBalance,
    int ExpectedBalance,
    int Difference,
    int MovementCount);

/// <summary>Extrato de movimentacao de estoque (UC-09).</summary>
public sealed record StockMovementResponse(
    Guid Id,
    Guid ProductId,
    string Code,
    string Description,
    int Quantity,
    int BalanceBefore,
    int BalanceAfter,
    Guid InvoiceId,
    DateTimeOffset CreatedAt);

public sealed record StockMovementPageResponse(
    IReadOnlyList<StockMovementResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
