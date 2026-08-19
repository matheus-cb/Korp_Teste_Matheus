using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Inventory.IntegrationTests;

public sealed class InventoryApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("inventory_tests")
        .WithUsername("inventory")
        .WithPassword("inventory")
        .Build();
    private WebApplicationFactory<Program>? factory;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:InventoryDatabase", database.GetConnectionString());
                builder.UseSetting("AllowedHosts", "localhost;127.0.0.1");
                builder.UseSetting("InternalAuth:AllowUnauthenticated", "true");
            });
        Client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        await database.DisposeAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InventoryApiTestGroup : ICollectionFixture<InventoryApiFixture>
{
    public const string Name = "Inventory API";
}
