using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Billing.Api.Options;
using Microsoft.Extensions.Options;

namespace Billing.Api.Tests;

public sealed class AiDraftServiceTests
{
    [Fact]
    public async Task Rejects_product_id_not_discovered_through_mcp()
    {
        var productId = Guid.NewGuid();
        var modelResult = new AiDraftModelResult(
            [new(productId, "FAKE", "Inventado", 1, "available")],
            [], [], [], new HashSet<Guid>(), [], 10, 10);
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        await using var db = factory.CreateDbContext();
        var service = new AiDraftService(
            db,
            new DeterministicFakeAiClient(modelResult),
            Microsoft.Extensions.Options.Options.Create(new OpenAiOptions { ApiKey = "test" }),
            TimeProvider.System,
            TestLoggers.For<AiDraftService>());

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CreateAsync("adicione um produto", null, CancellationToken.None));

        Assert.Contains("não foi descoberto", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AiDraftRunStatus.Failed, Assert.Single(db.AiDraftRuns).Status);
    }

    [Fact]
    public async Task Aggregates_duplicate_ai_items_after_validation()
    {
        var productId = Guid.NewGuid();
        var modelResult = new AiDraftModelResult(
            [
                new(productId, "P-1", "Produto", 2, "available"),
                new(productId, "P-1", "Produto", 3, "available")
            ],
            [], [], [], new HashSet<Guid> { productId }, ["search_products", "check_availability"], 10, 10);
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        await using var db = factory.CreateDbContext();
        var service = new AiDraftService(
            db,
            new DeterministicFakeAiClient(modelResult),
            Microsoft.Extensions.Options.Options.Create(new OpenAiOptions { ApiKey = "test" }),
            TimeProvider.System,
            TestLoggers.For<AiDraftService>());

        var response = await service.CreateAsync("cinco produtos", null, CancellationToken.None);

        Assert.Equal(5, Assert.Single(response.Items).Quantity);
        Assert.Equal(AiDraftRunStatus.Completed, Assert.Single(db.AiDraftRuns).Status);
    }

    [Fact]
    public async Task Reports_disabled_without_creating_a_run_when_no_provider_is_configured()
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        await using var db = factory.CreateDbContext();
        var service = new AiDraftService(
            db,
            // Quem responde por estar configurado agora e o provedor, e nao a
            // chave da OpenAI: sem isso qualquer provedor novo nasceria desligado.
            new DeterministicFakeAiClient(default!, isConfigured: false),
            Microsoft.Extensions.Options.Options.Create(new OpenAiOptions()),
            TimeProvider.System,
            TestLoggers.For<AiDraftService>());

        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            service.CreateAsync("um produto", null, CancellationToken.None));

        Assert.Equal("AI_DISABLED", exception.Code);
        Assert.Empty(db.AiDraftRuns);
    }
}
