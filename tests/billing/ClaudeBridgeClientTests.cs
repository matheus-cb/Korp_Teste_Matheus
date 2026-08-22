using System.Net;
using System.Text.Json;
using Billing.Api.Application;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Billing.Api.Options;

namespace Billing.Api.Tests;

public sealed class ClaudeBridgeClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "AI_BUSY")]
    [InlineData(HttpStatusCode.BadGateway, "AI_UNAVAILABLE")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "AI_UNAVAILABLE")]
    public async Task Bridge_failures_are_reported_as_dependency_errors(
        HttpStatusCode status,
        string expectedCode)
    {
        using var httpClient = new HttpClient(new StatusHandler(status))
        {
            BaseAddress = new Uri("http://bridge/")
        };
        var client = new ClaudeBridgeClient(
            httpClient,
            new EmptyToolSessionFactory(),
            new EmptyLocalTools(),
            Microsoft.Extensions.Options.Options.Create(new ClaudeBridgeOptions { BaseUrl = "http://bridge/", Secret = "secret" }),
            Microsoft.Extensions.Options.Options.Create(new OpenAiOptions { MaxToolCalls = 8 }),
            TestLoggers.For<ClaudeBridgeClient>());

        var error = await Assert.ThrowsAsync<DependencyUnavailableException>(() => client.RespondAsync(
            new AssistantClientRequest("O que tenho no estoque?", [], null),
            CancellationToken.None));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(503, error.StatusCode);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class EmptyToolSessionFactory : IInventoryToolSessionFactory
    {
        public Task<IInventoryToolSession> OpenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IInventoryToolSession>(new EmptyToolSession());
    }

    private sealed class EmptyToolSession : IInventoryToolSession
    {
        public IReadOnlyList<AiToolDefinition> Tools { get; } = [];

        public Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nenhuma ferramenta deveria ser chamada neste teste.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyLocalTools : IAssistantLocalTools
    {
        public IReadOnlyList<AiToolDefinition> Tools { get; } = [];

        public bool Owns(string name) => false;

        public Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Nenhuma ferramenta local deveria ser chamada neste teste.");
    }
}
