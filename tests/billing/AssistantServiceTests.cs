using System.Text.Json;
using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Billing.Api.Options;

namespace Billing.Api.Tests;

public sealed class AssistantServiceTests
{
    /// <summary>
    /// <c>AiDraftRun.ToolNames</c> é coluna <c>jsonb</c>. Uma string com vírgulas
    /// passa por compilação, por teste em memória e pelo CI — e só explode contra
    /// o PostgreSQL real, em produção, com "invalid input syntax for type json".
    /// Foi o que aconteceu; este teste é o que reprova a volta disso.
    /// </summary>
    [Fact]
    public async Task Registra_ferramentas_usadas_em_json_valido()
    {
        var (service, db) = Build(new AssistantClientReply(
            "Você tem 2 produtos.",
            false,
            null,
            [],
            [],
            [],
            [new AiDraftStep("list_products", "Listou o catálogo", "completed")],
            ["list_products", "list_products", "check_availability"],
            0,
            0));

        await service.RespondAsync(new AssistantMessageRequest("o que tenho?", []), CancellationToken.None);

        var run = Assert.Single(db.AiDraftRuns);
        var registradas = JsonSerializer.Deserialize<string[]>(run.ToolNames!);
        Assert.NotNull(registradas);
        // Distinct: a mesma ferramenta chamada duas vezes conta uma no registro.
        Assert.Equal(["list_products", "check_availability"], registradas);
    }

    [Fact]
    public async Task Conversa_sem_itens_nao_propoe_acao()
    {
        var (service, _) = Build(new AssistantClientReply(
            "Você tem 2 produtos no catálogo.", false, null, [], [], [], [], ["list_products"], 0, 0));

        var resposta = await service.RespondAsync(
            new AssistantMessageRequest("o que tenho no estoque?", []),
            CancellationToken.None);

        Assert.Null(resposta.Action);
        Assert.Empty(resposta.Items);
        Assert.Contains("2 produtos", resposta.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A proposta que chega ao servidor é sempre de criar; se um dia virar
    /// CreateAndCloseInvoice, a nota nasceria fechada sem ninguém pedir.
    /// </summary>
    [Fact]
    public async Task Proposta_de_nota_e_sempre_de_criar_e_nunca_de_fechar()
    {
        var productId = Guid.NewGuid();
        var (service, _) = Build(new AssistantClientReply(
            "Posso criar essa nota.",
            true,
            null,
            [new AiDraftModelItem(productId, "CAB-1", "Cabo", 2, "available")],
            [],
            [],
            [],
            ["search_products", "check_availability"],
            0,
            0));

        var resposta = await service.RespondAsync(
            new AssistantMessageRequest("crie uma nota com dois cabos", []),
            CancellationToken.None);

        Assert.NotNull(resposta.Action);
        Assert.Equal(nameof(ProposedActionKind.CreateInvoice), resposta.Action!.Kind);
        Assert.NotEqual(nameof(ProposedActionKind.CreateAndCloseInvoice), resposta.Action.Kind);
    }

    [Fact]
    public async Task Sem_provedor_configurado_responde_AI_DISABLED_sem_gravar_execucao()
    {
        var (service, db) = Build(null!, configured: false);

        var erro = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            service.RespondAsync(new AssistantMessageRequest("oi", []), CancellationToken.None));

        Assert.Equal("AI_DISABLED", erro.Code);
        Assert.Empty(db.AiDraftRuns);
    }

    [Fact]
    public async Task Historico_longo_e_cortado_antes_de_viajar()
    {
        var espiao = new EspiaoAssistantClient(new AssistantClientReply(
            "ok", false, null, [], [], [], [], [], 0, 0));
        var (service, _) = Build(espiao);

        var historico = Enumerable.Range(1, 30)
            .Select(i => new AssistantTurn(i % 2 == 0 ? "assistant" : "user", $"turno {i}"))
            .ToList();

        await service.RespondAsync(new AssistantMessageRequest("e agora?", historico), CancellationToken.None);

        // A ponte tem teto de prompt: mandar a conversa inteira estoura em uso real.
        Assert.Equal(10, espiao.UltimoPedido!.History.Count);
        Assert.Equal("turno 30", espiao.UltimoPedido.History[^1].Text);
    }

    private static (AssistantService Service, BillingDbContext Db) Build(
        AssistantClientReply reply,
        bool configured = true) =>
        Build(new FakeAssistantClient(reply, configured));

    private static (AssistantService Service, BillingDbContext Db) Build(IAssistantClient client)
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        var db = factory.CreateDbContext();
        var inventory = new FakeInventoryClient();
        var http = TestHttpContext.For("Ana Operadora");
        var invoices = new InvoiceService(db, inventory, TimeProvider.System, http);
        var closures = new ClosureCoordinator(
            factory, inventory, TimeProvider.System, http, TestLoggers.For<ClosureCoordinator>());
        var actions = new ProposedActionService(db, invoices, closures, inventory, TimeProvider.System, http);

        return (
            new AssistantService(
                db,
                client,
                actions,
                Microsoft.Extensions.Options.Options.Create(new OpenAiOptions()),
                TimeProvider.System,
                TestLoggers.For<AssistantService>()),
            db);
    }

    private sealed class FakeAssistantClient(AssistantClientReply reply, bool configured) : IAssistantClient
    {
        public bool IsConfigured => configured;
        public string ModelName => "fake";

        public Task<AssistantClientReply> RespondAsync(
            AssistantClientRequest request,
            CancellationToken cancellationToken) => Task.FromResult(reply);
    }

    private sealed class EspiaoAssistantClient(AssistantClientReply reply) : IAssistantClient
    {
        public AssistantClientRequest? UltimoPedido { get; private set; }
        public bool IsConfigured => true;
        public string ModelName => "fake";

        public Task<AssistantClientReply> RespondAsync(
            AssistantClientRequest request,
            CancellationToken cancellationToken)
        {
            UltimoPedido = request;
            return Task.FromResult(reply);
        }
    }
}
