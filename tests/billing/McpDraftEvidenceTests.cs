using System.Text.Json;
using Billing.Api.Application;
using Billing.Api.Infrastructure;

namespace Billing.Api.Tests;

/// <summary>
/// A seção 2.2 do docs/plano-agente-vps.md descreve uma armadilha: a validação
/// de proveniência é tão forte quanto a honestidade de quem implementa o
/// provedor, porque era ele quem preenchia <c>DiscoveredProductIds</c>. Um
/// provedor "generoso", que devolvesse todos os ids consultados — ou o catálogo
/// inteiro — faria a checagem passar vazia, com rascunhos plausíveis e nenhum
/// teste acusando.
///
/// Estes testes fecham isso: o conjunto passa a ser derivado de resultado de
/// ferramenta, e afirmar produto sem evidência é reprovado aqui.
/// </summary>
public sealed class McpDraftEvidenceTests
{
    private static readonly Guid Descoberto = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NuncaVisto = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Provedor_generoso_e_reprovado_ao_afirmar_produto_sem_evidencia()
    {
        var evidencia = ComCatalogoEDisponibilidade(quantidade: 2);

        var erro = Assert.Throws<InvalidOperationException>(() =>
            evidencia.BuildItems([(NuncaVisto, 2)]));

        Assert.Contains("not discovered through MCP", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Descobertos_saem_do_resultado_da_ferramenta_e_nao_da_palavra_do_provedor()
    {
        var evidencia = ComCatalogoEDisponibilidade(quantidade: 2);

        // Só o que a ferramenta devolveu está no conjunto.
        Assert.Contains(Descoberto, evidencia.DiscoveredProductIds);
        Assert.DoesNotContain(NuncaVisto, evidencia.DiscoveredProductIds);
    }

    [Fact]
    public void Item_sem_prova_de_disponibilidade_e_reprovado()
    {
        var evidencia = new McpDraftEvidence();
        evidencia.Capture("search_products", Vazio(), Resultado(CatalogoJson));

        // Produto conhecido, mas ninguém chamou check_availability para ele.
        var erro = Assert.Throws<InvalidOperationException>(() =>
            evidencia.BuildItems([(Descoberto, 2)]));

        Assert.Contains("availability proof", erro.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prova é por quantidade AGREGADA. Provar 2 e depois pedir 3 é pedir sem
    /// prova — e é o caminho por onde um saldo insuficiente passaria despercebido.
    /// </summary>
    [Fact]
    public void Prova_de_uma_quantidade_nao_vale_para_outra()
    {
        var evidencia = ComCatalogoEDisponibilidade(quantidade: 2);

        Assert.Throws<InvalidOperationException>(() => evidencia.BuildItems([(Descoberto, 3)]));
    }

    [Fact]
    public void Mesmo_produto_repetido_soma_e_exige_prova_do_total()
    {
        var comProvaDoTotal = ComCatalogoEDisponibilidade(quantidade: 5);

        var itens = comProvaDoTotal.BuildItems([(Descoberto, 2), (Descoberto, 3)]);

        Assert.Equal(2, itens.Count);
        Assert.All(itens, item => Assert.Equal("available", item.Availability));
    }

    [Fact]
    public void Codigo_e_descricao_vem_do_catalogo_e_nao_do_modelo()
    {
        var evidencia = ComCatalogoEDisponibilidade(quantidade: 2);

        var item = Assert.Single(evidencia.BuildItems([(Descoberto, 2)]));

        Assert.Equal("CAB-1", item.Code);
        Assert.Equal("Cabo USB-C", item.Description);
    }

    [Fact]
    public void Servidor_incoerente_e_recusado()
    {
        var evidencia = new McpDraftEvidence();
        evidencia.Capture("search_products", Vazio(), Resultado(CatalogoJson));

        // Diz que há saldo 1 para 2 pedidos e mesmo assim marca disponível.
        var incoerente = $$"""
            {"allAvailable":true,"items":[{"productId":"{{Descoberto}}","code":"CAB-1",
             "description":"Cabo USB-C","requestedQuantity":2,"availableBalance":1,
             "exists":true,"isAvailable":true}]}
            """;

        Assert.Throws<InvalidOperationException>(() =>
            evidencia.Capture("check_availability", ArgumentosDisponibilidade(2), Resultado(incoerente)));
    }

    [Fact]
    public void Get_product_exige_id_ja_descoberto()
    {
        var evidencia = new McpDraftEvidence();

        // Antes de qualquer busca, sondar por id é recusado.
        Assert.Throws<InvalidOperationException>(() =>
            evidencia.ValidateCall("get_product", Argumentos($$"""{"productId":"{{NuncaVisto}}"}""")));

        evidencia.Capture("search_products", Vazio(), Resultado(CatalogoJson));
        evidencia.ValidateCall("get_product", Argumentos($$"""{"productId":"{{Descoberto}}"}"""));
    }

    [Fact]
    public void Ferramenta_fora_da_allowlist_e_recusada()
    {
        var evidencia = new McpDraftEvidence();

        Assert.Throws<InvalidOperationException>(() => evidencia.ValidateCall("create_product", Vazio()));
        Assert.Throws<InvalidOperationException>(() => evidencia.ValidateCall("close_invoice", Vazio()));
    }

    [Fact]
    public void Erro_semantico_da_ferramenta_nao_vira_evidencia()
    {
        var evidencia = new McpDraftEvidence();

        Assert.Throws<InvalidOperationException>(() => evidencia.Capture(
            "search_products",
            Vazio(),
            Resultado("""{"products":[],"errorCode":"VALIDATION_ERROR","errorMessage":"query invalida"}""")));

        Assert.Empty(evidencia.DiscoveredProductIds);
    }

    private const string CatalogoJson = """
        {"products":[{"productId":"11111111-1111-1111-1111-111111111111",
         "code":"CAB-1","description":"Cabo USB-C","balance":10}]}
        """;

    private static McpDraftEvidence ComCatalogoEDisponibilidade(int quantidade)
    {
        var evidencia = new McpDraftEvidence();
        evidencia.Capture("search_products", Vazio(), Resultado(CatalogoJson));
        evidencia.Capture(
            "check_availability",
            ArgumentosDisponibilidade(quantidade),
            Resultado($$"""
                {"allAvailable":true,"items":[{"productId":"{{Descoberto}}","code":"CAB-1",
                 "description":"Cabo USB-C","requestedQuantity":{{quantidade}},"availableBalance":10,
                 "exists":true,"isAvailable":true}]}
                """));
        return evidencia;
    }

    private static JsonElement ArgumentosDisponibilidade(int quantidade) =>
        Argumentos($$"""{"items":[{"productId":"{{Descoberto}}","quantity":{{quantidade}}}]}""");

    private static JsonElement Argumentos(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Vazio() => Argumentos("{}");

    private static AiToolResult Resultado(string json) => new(Argumentos(json), false);
}
