namespace Billing.Api.Domain;

public enum InvoiceStatus
{
    Open = 0,
    Closed = 1
}

public enum ClosureAttemptState
{
    Pending = 0,
    Completed = 1,
    Rejected = 2
}

public enum AiDraftRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2
}

public sealed class Invoice
{
    private Invoice() { }

    public Guid Id { get; private set; }
    public long Number { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Quem confirmou a criação e quem confirmou o fechamento.</summary>
    public string CreatedBy { get; private set; } = "sistema";
    public string? ClosedBy { get; private set; }
    public string UpdatedBy { get; private set; } = "sistema";

    public Guid Version { get; private set; }
    public List<InvoiceItem> Items { get; private set; } = [];
    public List<InvoiceClosureAttempt> ClosureAttempts { get; private set; } = [];
    public List<InvoiceAuditEvent> AuditEvents { get; private set; } = [];

    public static Invoice Create(
        IEnumerable<ProductSnapshot> products,
        TimeProvider clock,
        string createdBy = "sistema")
    {
        var normalized = products
            .GroupBy(x => x.ProductId)
            .Select(group =>
            {
                var first = group.First();
                return first with { Quantity = checked(group.Sum(x => x.Quantity)) };
            })
            .ToList();

        if (normalized.Count is < 1 or > 20)
            throw new DomainValidationException("A nota deve conter entre 1 e 20 produtos.");
        if (normalized.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0))
            throw new DomainValidationException("Todos os produtos e quantidades devem ser válidos.");

        var now = clock.GetUtcNow();
        var actor = string.IsNullOrWhiteSpace(createdBy) ? "sistema" : createdBy.Trim();
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            Status = InvoiceStatus.Open,
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
            Version = Guid.NewGuid()
        };

        invoice.Items = normalized.Select(x => InvoiceItem.Create(invoice.Id, x)).ToList();
        invoice.AuditEvents.Add(InvoiceAuditEvent.Create(invoice.Id, "Created", invoice.CreatedBy, invoice.CreatedAt));
        return invoice;
    }

    public void ReplaceItems(IEnumerable<ProductSnapshot> products, Guid expectedVersion, TimeProvider clock, string updatedBy)
    {
        if (Status != InvoiceStatus.Open)
            throw new ConflictException("INVOICE_NOT_OPEN", "Somente notas abertas podem ser editadas.");
        if (ClosureAttempts.Any(attempt => attempt.State == ClosureAttemptState.Pending))
            throw new ConflictException("INVOICE_CLOSURE_PENDING", "A nota está em fechamento e não pode ser editada.");
        if (expectedVersion != Version)
            throw new ConflictException("INVOICE_VERSION_CONFLICT", "A nota foi alterada por outra pessoa. Atualize os dados antes de salvar.");

        var normalized = products.GroupBy(x => x.ProductId).Select(group =>
        {
            var first = group.First();
            return first with { Quantity = checked(group.Sum(x => x.Quantity)) };
        }).ToList();
        if (normalized.Count is < 1 or > 20 || normalized.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0))
            throw new DomainValidationException("A nota deve conter entre 1 e 20 produtos com quantidades válidas.");

        Items = normalized.Select(item => InvoiceItem.Create(Id, item)).ToList();
        var now = clock.GetUtcNow();
        UpdatedAt = now;
        UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "sistema" : updatedBy.Trim();
        Version = Guid.NewGuid();
    }

    public void Close(DateTimeOffset now, string? closedBy = null)
    {
        if (Status == InvoiceStatus.Closed)
            return;
        Status = InvoiceStatus.Closed;
        ClosedAt = now;
        ClosedBy = string.IsNullOrWhiteSpace(closedBy) ? ClosedBy : closedBy.Trim();
        UpdatedAt = now;
        UpdatedBy = ClosedBy ?? UpdatedBy;
        Version = Guid.NewGuid();
    }
}

/// <summary>Histórico somente de acréscimo; a nota preserva quem e quando a alterou.</summary>
public sealed class InvoiceAuditEvent
{
    private InvoiceAuditEvent() { }
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string ActorName { get; private set; } = "sistema";
    public DateTimeOffset OccurredAt { get; private set; }

    public static InvoiceAuditEvent Create(Guid invoiceId, string type, string actorName, DateTimeOffset occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = invoiceId,
        Type = type,
        ActorName = string.IsNullOrWhiteSpace(actorName) ? "sistema" : actorName.Trim(),
        OccurredAt = occurredAt
    };
}

public sealed record ProductSnapshot(Guid ProductId, string Code, string Description, int Quantity);

public sealed class InvoiceItem
{
    private InvoiceItem() { }
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductDescription { get; private set; } = string.Empty;
    public int Quantity { get; private set; }

    internal static InvoiceItem Create(Guid invoiceId, ProductSnapshot product) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = invoiceId,
        ProductId = product.ProductId,
        ProductCode = product.Code.Trim(),
        ProductDescription = product.Description.Trim(),
        Quantity = product.Quantity
    };
}

public sealed class InvoiceClosureAttempt
{
    /// <summary>Itens que entraram na nota sem movimentar estoque (INV-04).</summary>
    public string? IgnoredItemsJson { get; private set; }

    public void RecordIgnoredItems(string? json) => IgnoredItemsJson = json;

    private InvoiceClosureAttempt() { }
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public ClosureAttemptState State { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset NextRetryAt { get; private set; }
    public Guid Version { get; private set; }

    public static InvoiceClosureAttempt Start(Guid invoiceId, string payloadHash, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = invoiceId,
        PayloadHash = payloadHash,
        State = ClosureAttemptState.Pending,
        CreatedAt = now,
        UpdatedAt = now,
        NextRetryAt = now,
        Version = Guid.NewGuid()
    };

    public void RecordTransientFailure(string code, string message, DateTimeOffset now)
    {
        State = ClosureAttemptState.Pending;
        ErrorCode = code;
        ErrorMessage = message;
        RetryCount++;
        UpdatedAt = now;
        var seconds = Math.Min(60, Math.Pow(2, Math.Min(RetryCount, 5)));
        NextRetryAt = now.AddSeconds(seconds);
        Version = Guid.NewGuid();
    }

    public void Complete(DateTimeOffset now)
    {
        State = ClosureAttemptState.Completed;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void Reject(string code, string message, DateTimeOffset now)
    {
        State = ClosureAttemptState.Rejected;
        ErrorCode = code;
        ErrorMessage = message;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public sealed class AiDraftRun
{
    private AiDraftRun() { }
    public Guid Id { get; private set; }
    public AiDraftRunStatus Status { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public string ToolNames { get; private set; } = "[]";
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal EstimatedCostUsd { get; private set; }
    public long DurationMilliseconds { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AiDraftRun Start(string model, string promptVersion, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Status = AiDraftRunStatus.Running,
        Model = model,
        PromptVersion = promptVersion,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void Complete(string toolNames, int inputTokens, int outputTokens, decimal estimatedCost, long elapsedMs, DateTimeOffset now)
    {
        Status = AiDraftRunStatus.Completed;
        ToolNames = toolNames;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUsd = estimatedCost;
        DurationMilliseconds = elapsedMs;
        UpdatedAt = now;
    }

    public void Fail(string code, string toolNames, long elapsedMs, DateTimeOffset now)
    {
        Status = AiDraftRunStatus.Failed;
        FailureCode = code;
        ToolNames = toolNames;
        DurationMilliseconds = elapsedMs;
        UpdatedAt = now;
    }
}

/// <summary>Registro de importação de notas por CSV, para idempotência por arquivo.</summary>
public sealed class InvoiceImport
{
    private InvoiceImport() { }

    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public int CreatedInvoices { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = "sistema";

    public static InvoiceImport Create(
        string fileName,
        string contentHash,
        int createdInvoices,
        DateTimeOffset now,
        string createdBy) => new()
        {
            Id = Guid.NewGuid(),
            FileName = fileName.Length > 260 ? fileName[..260] : fileName,
            ContentHash = contentHash,
            CreatedInvoices = createdInvoices,
            CreatedAt = now,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "sistema" : createdBy,
        };
}
