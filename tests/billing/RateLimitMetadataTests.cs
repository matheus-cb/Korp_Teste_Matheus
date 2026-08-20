using Billing.Api.Api;
using Billing.Api.Application;
using Billing.Api.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Api.Tests;

public sealed class RateLimitMetadataTests
{
    [Fact]
    public async Task Ai_draft_routes_are_limited_but_invoice_routes_are_not()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<AiDraftService>(_ => null!);
        builder.Services.AddScoped<AssistantService>(_ => null!);
        builder.Services.AddScoped<InvoiceService>(_ => null!);
        builder.Services.AddScoped<ClosureCoordinator>(_ => null!);
        builder.Services.AddScoped<BillingDbContext>(_ => null!);
        builder.Services.AddScoped<IInvoicePdfGenerator>(_ => null!);
        await using var app = builder.Build();
        app.MapAiEndpoints();
        app.MapInvoiceEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var canonicalAiRoute = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/invoices/ai-draft");
        var compatibilityAiRoute = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/ai-drafts");
        // O assistente conversacional chama o modelo como o rascunho: sem teto,
        // uma aba aberta consegue enfileirar execuções sem limite.
        var assistantRoute = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/assistant/messages");
        var closeRoute = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == "/api/invoices/{id:guid}/close");

        Assert.Equal(
            AiEndpoints.RateLimitPolicy,
            canonicalAiRoute.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal(
            AiEndpoints.RateLimitPolicy,
            compatibilityAiRoute.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal(
            AiEndpoints.RateLimitPolicy,
            assistantRoute.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Null(closeRoute.Metadata.GetMetadata<EnableRateLimitingAttribute>());
    }
}
