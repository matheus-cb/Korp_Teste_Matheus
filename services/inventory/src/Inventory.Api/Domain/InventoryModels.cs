namespace Inventory.Api.Domain;

public sealed class Product
{
    private Product()
    {
    }

    private Product(
        Guid id,
        string code,
        string description,
        int balance,
        bool tracksStock,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        Description = description;
        Balance = balance;
        TracksStock = tracksStock;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Balance { get; private set; }

    /// <summary>
    /// Quando falso, o item entra na nota mas não movimenta estoque: é o caso de
    /// serviço, brinde ou item sob encomenda. O saldo deixa de ser validado e
    /// nenhum movimento é gerado — o invariante de saldo não negativo continua
    /// valendo para os produtos controlados.
    /// </summary>
    public bool TracksStock { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Product Create(
        string code,
        string description,
        int balance,
        DateTimeOffset createdAt,
        bool tracksStock = true)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedDescription = description?.Trim() ?? string.Empty;

        if (normalizedCode.Length is < 1 or > 64)
        {
            throw new ArgumentException("Product code must contain between 1 and 64 characters.", nameof(code));
        }

        if (normalizedDescription.Length is < 1 or > 200)
        {
            throw new ArgumentException("Product description must contain between 1 and 200 characters.", nameof(description));
        }

        if (balance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(balance), "Product balance cannot be negative.");
        }

        // Produto sem controle não carrega saldo: manter um número ali daria a
        // impressão de que existe estoque a ser consumido.
        var effectiveBalance = tracksStock ? balance : 0;

        return new Product(
            Guid.NewGuid(),
            normalizedCode,
            normalizedDescription,
            effectiveBalance,
            tracksStock,
            createdAt);
    }

    public void Debit(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Debit quantity must be positive.");
        }

        if (!TracksStock)
        {
            throw new InvalidOperationException("Product does not track stock and cannot be debited.");
        }

        if (Balance < quantity)
        {
            throw new InvalidOperationException("Product has insufficient stock.");
        }

        Balance -= quantity;
    }

    /// <summary>Há saldo suficiente, ou o produto simplesmente não controla estoque.</summary>
    public bool CanFulfill(int quantity) => !TracksStock || Balance >= quantity;

    public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}

public enum StockDebitState
{
    Pending,
    Completed,
    Rejected
}

public sealed class StockDebitOperation
{
    private StockDebitOperation()
    {
    }

    private StockDebitOperation(
        Guid id,
        Guid attemptId,
        Guid invoiceId,
        string requestHash,
        DateTimeOffset createdAt)
    {
        Id = id;
        AttemptId = attemptId;
        InvoiceId = invoiceId;
        RequestHash = requestHash;
        State = StockDebitState.Pending;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public StockDebitState State { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>
    /// Itens que entraram na nota sem movimentar estoque, serializados em JSON.
    /// Precisam ser persistidos: a requisição original não é guardada, então uma
    /// repetição idempotente não teria como reconstruí-los — e a resposta
    /// repetida tem de ser igual à primeira.
    /// </summary>
    public string? IgnoredItemsJson { get; private set; }

    public ICollection<StockMovement> Movements { get; private set; } = new List<StockMovement>();

    public void RecordIgnoredItems(string? json) => IgnoredItemsJson = json;

    public static StockDebitOperation Start(
        Guid attemptId,
        Guid invoiceId,
        string requestHash,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), attemptId, invoiceId, requestHash, createdAt);

    public void Complete(DateTimeOffset finishedAt)
    {
        EnsurePending();
        State = StockDebitState.Completed;
        FinishedAt = finishedAt;
    }

    public void Reject(string errorCode, string errorMessage, DateTimeOffset finishedAt)
    {
        EnsurePending();
        State = StockDebitState.Rejected;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        FinishedAt = finishedAt;
    }

    private void EnsurePending()
    {
        if (State != StockDebitState.Pending)
        {
            throw new InvalidOperationException("Only a pending stock debit can change state.");
        }
    }
}

public sealed class StockMovement
{
    private StockMovement()
    {
    }

    private StockMovement(
        Guid id,
        Guid operationId,
        Guid productId,
        int quantity,
        int balanceBefore,
        int balanceAfter,
        DateTimeOffset createdAt)
    {
        Id = id;
        OperationId = operationId;
        ProductId = productId;
        Quantity = quantity;
        BalanceBefore = balanceBefore;
        BalanceAfter = balanceAfter;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public int BalanceBefore { get; private set; }
    public int BalanceAfter { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public StockDebitOperation Operation { get; private set; } = null!;
    public Product Product { get; private set; } = null!;

    public static StockMovement Create(
        Guid operationId,
        Guid productId,
        int quantity,
        int balanceBefore,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            operationId,
            productId,
            quantity,
            balanceBefore,
            checked(balanceBefore - quantity),
            createdAt);
}
