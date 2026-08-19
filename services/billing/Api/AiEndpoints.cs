using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;

namespace Billing.Api.Api;

public static class AiEndpoints
{
    public const string RateLimitPolicy = "ai-draft";

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapDraftEndpoint(endpoints, "/api/invoices/ai-draft");
        MapDraftEndpoint(endpoints, "/api/ai-drafts");
        return endpoints;
    }

    private static void MapDraftEndpoint(IEndpointRouteBuilder endpoints, string pattern)
    {
        endpoints.MapPost(pattern, CreateDraftAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<AiDraftResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("AI drafts");
    }

    private static async Task<IResult> CreateDraftAsync(
        HttpRequest request,
        AiDraftService service,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            throw new BadHttpRequestException("Use multipart/form-data.");
        var form = await request.ReadFormAsync(cancellationToken);
        var text = form["text"].FirstOrDefault();
        var imageFiles = form.Files.Where(file => file.Name == "image").ToList();
        if (imageFiles.Count > 1 || form.Files.Count != imageFiles.Count)
            throw new DomainValidationException("Envie no máximo uma imagem no campo image.");
        var image = imageFiles.SingleOrDefault();
        var response = await service.CreateAsync(text, image, cancellationToken);
        return Results.Ok(response);
    }
}
