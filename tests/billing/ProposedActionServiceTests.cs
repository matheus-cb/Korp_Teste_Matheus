using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;

namespace Billing.Api.Tests;

/// <summary>
/// A confirmação é controle de SERVIDOR. Estes testes existem porque, com a IA
/// podendo escrever, a assinatura é o que separa "o operador confirmou" de
/// "alguém chamou o endpoint direto".
/// </summary>
public sealed class ProposedActionServiceTests
{
    [Fact]
    public void Proposta_recebe_token_assinado_e_prazo()
    {
        var (service, _) = Build();
        var item = new ProposedItem(Guid.NewGuid(), "CAB-1", "Cabo", 2);

        var proposal = service.Propose(ProposedActionKind.CreateInvoice, [item]);

        Assert.False(string.IsNullOrWhiteSpace(proposal.Token));
        Assert.Contains(".", proposal.Token, StringComparison.Ordinal);
        Assert.True(proposal.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal("CreateInvoice", proposal.Kind);
    }

    [Fact]
    public async Task Token_adulterado_e_recusado()
    {
        var (service, _) = Build();
        var proposal = service.Propose(
            ProposedActionKind.CreateInvoice,
            [new ProposedItem(Guid.NewGuid(), "CAB-1", "Cabo", 1)]);

        // Troca o payload mantendo a assinatura: é o ataque que a assinatura impede.
        var parts = proposal.Token.Split('.');
        var forged = $"{parts[0]}x.{parts[1]}";

        var exception = await Assert.ThrowsAnyAsync<AppException>(
            () => service.ConfirmAsync(forged, CancellationToken.None));

        Assert.Equal("VALIDATION_ERROR", exception.Code);
    }

    [Fact]
    public async Task Token_de_outra_origem_e_recusado()
    {
        var (service, _) = Build();

        await Assert.ThrowsAnyAsync<AppException>(
            () => service.ConfirmAsync("payload-inventado.assinatura-inventada", CancellationToken.None));
    }

    [Fact]
    public void Proposta_sem_itens_e_recusada()
    {
        var (service, _) = Build();

        Assert.Throws<DomainValidationException>(
            () => service.Propose(ProposedActionKind.CreateInvoice, []));
    }

    [Fact]
    public void Proposta_com_quantidade_absurda_e_recusada()
    {
        var (service, _) = Build();
        var item = new ProposedItem(Guid.NewGuid(), "CAB-1", "Cabo", 1_000_001);

        Assert.Throws<DomainValidationException>(
            () => service.Propose(ProposedActionKind.CreateInvoice, [item]));
    }

    [Fact]
    public void Proposta_de_produto_e_assinada_e_saneada()
    {
        var (service, _) = Build();

        var proposal = service.ProposeProducts([new ProposedProduct("  CAB-9  ", "  Cabo novo  ", 5, true)]);

        Assert.Equal("CreateProduct", proposal.Kind);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Token));
        // Espaço nas pontas some antes de assinar: o token carrega o valor final.
        Assert.Equal("CAB-9", Assert.Single(proposal.Products).Code);
        Assert.Equal("Cabo novo", Assert.Single(proposal.Products).Description);
        Assert.Empty(proposal.Items);
    }

    [Theory]
    [InlineData("", "Cabo", 0)]
    [InlineData("CAB-1", "", 0)]
    [InlineData("CAB-1", "Cabo", -1)]
    public void Produto_com_dados_invalidos_nao_e_proposto(string code, string description, int balance)
    {
        var (service, _) = Build();

        Assert.Throws<DomainValidationException>(() =>
            service.ProposeProducts([new ProposedProduct(code, description, balance, true)]));
    }

    [Fact]
    public async Task Confirmar_produto_cria_no_inventory_e_nao_fecha_nota()
    {
        var (service, inventory) = Build();
        var proposal = service.ProposeProducts([new ProposedProduct("CAB-9", "Cabo novo", 5, true)]);

        var result = await service.ConfirmAsync(proposal.Token, CancellationToken.None);

        // Produto é domínio do Inventory (INV-02): a criação passa pela API dele.
        var created = Assert.Single(inventory.Created);
        Assert.Equal("CAB-9", created.Code);
        Assert.Equal(5, created.Balance);
        Assert.False(result.Closed);
    }

    [Fact]
    public async Task Lote_de_produtos_e_criado_de_uma_vez()
    {
        var (service, inventory) = Build();
        var proposal = service.ProposeProducts([
            new ProposedProduct("CAB-1", "Cabo", 5, true),
            new ProposedProduct("TEC-1", "Teclado", 2, true),
            new ProposedProduct("MON-1", "Monitor", 0, true)
        ]);

        var result = await service.ConfirmAsync(proposal.Token, CancellationToken.None);

        Assert.Equal(3, inventory.Created.Count);
        Assert.Equal(["CAB-1", "TEC-1", "MON-1"], inventory.Created.Select(p => p.Code));
        Assert.Equal(3, result.Number);
        Assert.False(result.Closed);
    }

    /// <summary>
    /// Código repetido criaria o primeiro e falharia no segundo, deixando o lote
    /// metade aplicado. Recusar antes de assinar é mais barato que reconciliar.
    /// </summary>
    [Fact]
    public void Lote_com_codigo_repetido_e_recusado_antes_de_assinar()
    {
        var (service, inventory) = Build();

        Assert.Throws<DomainValidationException>(() => service.ProposeProducts([
            new ProposedProduct("CAB-1", "Cabo", 5, true),
            new ProposedProduct("cab-1", "Cabo de novo", 2, true)
        ]));

        Assert.Empty(inventory.Created);
    }

    [Fact]
    public void Lote_vazio_ou_grande_demais_e_recusado()
    {
        var (service, _) = Build();

        Assert.Throws<DomainValidationException>(() => service.ProposeProducts([]));
        Assert.Throws<DomainValidationException>(() => service.ProposeProducts(
            Enumerable.Range(1, 21).Select(i => new ProposedProduct($"P-{i}", $"Produto {i}", 0, true)).ToList()));
    }

    /// <summary>
    /// O assistente propõe com <see cref="ProposedActionKind.CreateInvoice"/>. Se um dia
    /// alguém trocar isso por CreateAndCloseInvoice, a nota passaria a nascer fechada
    /// sem que ninguém pedisse — e fechar é justamente o que ele não pode fazer.
    /// </summary>
    [Fact]
    public async Task Nota_proposta_nasce_aberta_e_o_assistente_nao_fecha()
    {
        var (service, inventory) = Build();
        var productId = Guid.NewGuid();
        inventory.Products[productId] = new InventoryProduct(productId, "CAB-1", "Cabo", 10);
        var proposal = service.Propose(
            ProposedActionKind.CreateInvoice,
            [new ProposedItem(productId, "CAB-1", "Cabo", 2)]);

        var result = await service.ConfirmAsync(proposal.Token, CancellationToken.None);

        Assert.False(result.Closed);
        Assert.Equal(InvoiceStatus.Open.ToString(), result.Status);
        Assert.Equal(0, inventory.DebitCalls);
    }

    private static (ProposedActionService Service, FakeInventoryClient Inventory) Build()
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        var db = factory.CreateDbContext();
        var inventory = new FakeInventoryClient();
        var http = TestHttpContext.For("Ana Operadora");
        var invoices = new InvoiceService(db, inventory, TimeProvider.System, http);
        var closures = new ClosureCoordinator(
            factory,
            inventory,
            TimeProvider.System,
            http,
            TestLoggers.For<ClosureCoordinator>());

        return (
            new ProposedActionService(db, invoices, closures, inventory, TimeProvider.System, http),
            inventory);
    }
}
