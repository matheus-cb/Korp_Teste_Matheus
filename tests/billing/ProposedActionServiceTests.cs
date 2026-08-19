using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;

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
