using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Billing.Api.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Billing.IntegrationTests;

public sealed class BillingApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("billing_tests")
        .WithUsername("billing")
        .WithPassword("billing")
        .Build();
    private WebApplicationFactory<Program>? factory;

    public FakeInventoryServer Inventory { get; } = new();
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => factory?.Services
        ?? throw new InvalidOperationException("The Billing test host has not started.");

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        await Inventory.StartAsync();

        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("AllowedHosts", "*");
                builder.UseSetting("ConnectionStrings:Billing", database.GetConnectionString());
                builder.UseSetting("Database:MigrateOnStartup", "true");
                builder.UseSetting("Inventory:BaseUrl", Inventory.BaseAddress.AbsoluteUri);
                builder.UseSetting("Inventory:McpEndpoint", new Uri(Inventory.BaseAddress, "/mcp").AbsoluteUri);
                builder.UseSetting("Inventory:TimeoutSeconds", "20");
                builder.UseSetting("InternalAuth:Token", string.Empty);
                builder.UseSetting("InternalAuth:AllowUnauthenticated", "true");
                builder.ConfigureTestServices(services =>
                {
                    // These tests drive reconciliation explicitly so timing does not depend
                    // on the five-second production worker interval.
                    var worker = services.FirstOrDefault(descriptor =>
                        descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType == typeof(ClosureReconciliationWorker));
                    if (worker is not null) services.Remove(worker);
                });
            });

        Client = factory.CreateClient();
        await AuthenticateAsync();
    }

    /// <summary>
    /// Os endpoints de negócio passaram a exigir sessão. O fixture faz login
    /// com um dos usuários semeados e fixa o token para toda a suíte, do mesmo
    /// jeito que o frontend faz.
    /// </summary>
    private async Task AuthenticateAsync()
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/auth/login",
            new { userName = "operador", password = "notaflow123" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("token").GetString();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (factory is not null)
            await factory.DisposeAsync();
        await Inventory.DisposeAsync();
        await database.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BillingApiTestGroup : ICollectionFixture<BillingApiFixture>
{
    public const string Name = "Billing API integration";
}
