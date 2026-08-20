using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Billing.Api.Api;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Application;

/// <summary>
/// Escrita assistida por IA, por ação proposta.
///
/// O assistente não executa nada. Ele devolve uma <b>ação proposta</b> tipada e
/// assinada; a interface mostra exatamente o que será feito; e só depois de o
/// operador confirmar é que o backend executa, reaproveitando as validações,
/// a saga e a chave idempotente que já existem.
///
/// Por que não uma tool MCP de escrita: o servidor MCP é do Inventory e criar
/// ou fechar nota é domínio do Billing, que é o CLIENTE MCP — uma tool assim
/// faria o Billing chamar a si mesmo pelo protocolo.
///
/// A confirmação é controle de SERVIDOR, não de interface: a ação carrega uma
/// assinatura HMAC e prazo de validade. Sem isso, prompt injection contornaria
/// a confirmação chamando o endpoint de execução direto.
/// </summary>
public sealed class ProposedActionService(
    BillingDbContext db,
    InvoiceService invoices,
    ClosureCoordinator closures,
    IInventoryClient inventory,
    TimeProvider clock,
    IHttpContextAccessor httpContext)
{
    private static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Chave de assinatura da sessão do processo. Proposta não sobrevive a
    /// reinício do serviço — o que é desejável: ela vale por minutos.
    /// </summary>
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);

    public ProposedActionResponse Propose(ProposedActionKind kind, IReadOnlyList<ProposedItem> items)
    {
        if (items.Count is < 1 or > 20)
        {
            throw new DomainValidationException("A ação proposta deve conter entre 1 e 20 itens.");
        }

        if (items.Any(item => item.Quantity is <= 0 or > 1_000_000))
        {
            throw new DomainValidationException("Quantidade inválida na ação proposta.");
        }

        var payload = new ProposedActionPayload(
            kind,
            items,
            null,
            clock.GetUtcNow().Add(Validity));

        var encoded = Encode(payload);
        return new ProposedActionResponse(kind.ToString(), items, [], payload.ExpiresAt, encoded);
    }

    /// <summary>
    /// Propõe cadastrar um produto novo. Não há proveniência MCP a checar — o
    /// produto ainda não existe — então a validação aqui é de formato, espelhando
    /// os limites de <c>CreateProductRequest</c> no Inventory, e a garantia real
    /// continua sendo a confirmação humana (INV-24).
    /// </summary>
    public ProposedActionResponse ProposeProducts(IReadOnlyList<ProposedProduct> products)
    {
        if (products.Count is < 1 or > 20)
            throw new DomainValidationException("Proponha entre 1 e 20 produtos por vez.");

        var saneados = products.Select(Sanear).ToList();

        // Código repetido na mesma proposta criaria o primeiro e falharia no
        // segundo, deixando metade cadastrada — melhor recusar antes.
        var duplicado = saneados
            .GroupBy(product => product.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicado is not null)
            throw new DomainValidationException($"O código {duplicado.Key} aparece mais de uma vez na proposta.");

        var payload = new ProposedActionPayload(
            ProposedActionKind.CreateProduct,
            [],
            saneados,
            clock.GetUtcNow().Add(Validity));

        return new ProposedActionResponse(
            ProposedActionKind.CreateProduct.ToString(),
            [],
            saneados,
            payload.ExpiresAt,
            Encode(payload));
    }

    private static ProposedProduct Sanear(ProposedProduct product)
    {
        var code = product.Code?.Trim() ?? string.Empty;
        var description = product.Description?.Trim() ?? string.Empty;

        if (code.Length is < 1 or > 64)
            throw new DomainValidationException("O código do produto deve ter entre 1 e 64 caracteres.");
        if (description.Length is < 1 or > 200)
            throw new DomainValidationException("A descrição do produto deve ter entre 1 e 200 caracteres.");
        if (product.Balance is < 0 or > 1_000_000)
            throw new DomainValidationException($"Saldo inicial inválido para o produto {code}.");

        return new ProposedProduct(code, description, product.Balance, product.TracksStock);
    }

    /// <summary>Executa a ação apenas se a assinatura e o prazo conferirem.</summary>
    public async Task<ProposedActionResultResponse> ConfirmAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var payload = Decode(token);

        if (clock.GetUtcNow() > payload.ExpiresAt)
        {
            throw new DomainValidationException("A ação proposta expirou. Peça um novo rascunho ao assistente.");
        }

        if (payload.Kind == ProposedActionKind.CreateProduct)
        {
            if (payload.Products is not { Count: > 0 } propostos)
                throw new DomainValidationException("A ação proposta não descreve produto nenhum.");

            // Produto é domínio do Inventory (INV-02): criamos pela API dele, nunca
            // escrevendo no banco do outro serviço (INV-05).
            //
            // Não há transação entre serviços: se o terceiro de cinco falhar, os
            // dois primeiros ficam criados. Em vez de fingir atomicidade, o erro
            // diz quantos entraram, para a pessoa saber o que sobrou por fazer.
            var criados = new List<Guid>(propostos.Count);
            foreach (var proposto in propostos)
            {
                try
                {
                    var criado = await inventory.CreateProductAsync(
                        proposto.Code,
                        proposto.Description,
                        proposto.Balance,
                        proposto.TracksStock,
                        cancellationToken);
                    criados.Add(criado.Id);
                }
                catch (Exception erro) when (erro is ConflictException or DomainValidationException)
                {
                    throw new ConflictException(
                        "PARTIAL_PRODUCT_CREATION",
                        criados.Count == 0
                            ? $"Nenhum produto foi criado: {erro.Message}"
                            : $"{criados.Count} de {propostos.Count} produtos foram criados; parou em {proposto.Code}: {erro.Message}");
                }
            }

            return new ProposedActionResultResponse(
                criados[0],
                criados.Count,
                "ProductCreated",
                false,
                httpContext.ActingUserName());
        }

        // Revalida tudo do zero: a proposta é uma sugestão, não uma autorização
        // para pular validação de domínio.
        foreach (var item in payload.Items)
        {
            var product = await inventory.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new ResourceNotFoundException(
                    "PRODUCT_NOT_FOUND",
                    $"O produto {item.ProductId} não existe mais no catálogo.");

            if (product.TracksStock && product.Balance < item.Quantity)
            {
                throw new ConflictException(
                    "INSUFFICIENT_STOCK",
                    $"Saldo insuficiente para {product.Code}: disponível {product.Balance}, solicitado {item.Quantity}.");
            }
        }

        var invoice = await invoices.CreateAsync(
            new CreateInvoiceRequest(
                payload.Items
                    .Select(item => new CreateInvoiceItemRequest(item.ProductId, item.Quantity))
                    .ToList()),
            cancellationToken);

        var closed = false;
        if (payload.Kind == ProposedActionKind.CreateAndCloseInvoice)
        {
            var (_, attempt) = await invoices.BeginClosureAsync(invoice.Id, cancellationToken);
            var result = await closures.ProcessAsync(attempt.Id, true, cancellationToken);
            closed = result.State == ClosureAttemptState.Completed;
        }

        var stored = await db.Invoices
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoice.Id, cancellationToken);

        return new ProposedActionResultResponse(
            stored.Id,
            stored.Number,
            stored.Status.ToString(),
            closed,
            httpContext.ActingUserName());
    }

    private static string Encode(ProposedActionPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        using var hmac = new HMACSHA256(SigningKey);
        var signature = hmac.ComputeHash(json);
        return $"{Base64Url(json)}.{Base64Url(signature)}";
    }

    private static ProposedActionPayload Decode(string token)
    {
        var parts = (token ?? string.Empty).Split('.');
        if (parts.Length != 2)
        {
            throw new DomainValidationException("Ação proposta inválida.");
        }

        byte[] json;
        byte[] signature;
        try
        {
            json = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            throw new DomainValidationException("Ação proposta inválida.");
        }

        using var hmac = new HMACSHA256(SigningKey);
        var expected = hmac.ComputeHash(json);

        // Tempo constante: a assinatura é o que impede execução sem confirmação.
        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            throw new DomainValidationException("Ação proposta inválida ou adulterada.");
        }

        return JsonSerializer.Deserialize<ProposedActionPayload>(json, Json)
            ?? throw new DomainValidationException("Ação proposta inválida.");
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record ProposedActionPayload(
        ProposedActionKind Kind,
        IReadOnlyList<ProposedItem> Items,
        IReadOnlyList<ProposedProduct>? Products,
        DateTimeOffset ExpiresAt);
}
