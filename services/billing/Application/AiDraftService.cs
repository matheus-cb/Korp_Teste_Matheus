using System.Diagnostics;
using System.Text.Json;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Billing.Api.Options;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Billing.Api.Application;

public interface IInvoiceDraftAiClient
{
    /// <summary>
    /// Se o provedor tem credencial para operar. Quem decide e o provedor:
    /// antes, o serviço olhava direto para a chave da OpenAI, o que deixava
    /// qualquer outro provedor permanentemente desligado (INV-23).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Modelo efetivo, registrado em AiDraftRun para auditoria.</summary>
    string ModelName { get; }

    /// <summary>Se o provedor aceita imagem. Ver seção 6.1 do plano da VPS:
    /// o atalho "Ler de uma foto" não pode falhar em silêncio.</summary>
    bool SupportsImage { get; }

    Task<AiDraftModelResult> GenerateAsync(AiDraftInput input, CancellationToken cancellationToken);
}

public sealed class AiDraftService(
    BillingDbContext db,
    IInvoiceDraftAiClient aiClient,
    IOptions<OpenAiOptions> options,
    TimeProvider clock,
    ILogger<AiDraftService> logger)
{
    public async Task<AiDraftResponse> CreateAsync(string? text, IFormFile? image, CancellationToken cancellationToken)
    {
        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (text is { Length: > 2000 })
            throw new DomainValidationException("O texto deve ter no máximo 2.000 caracteres.");
        if (text is null && image is null)
            throw new DomainValidationException("Informe um texto ou uma imagem do pedido.");

        var configured = options.Value;
        if (!aiClient.IsConfigured)
            throw new DependencyUnavailableException(
                "AI_DISABLED",
                "O copiloto está desabilitado porque nenhum provedor de IA foi configurado.");

        if (image is not null && !aiClient.SupportsImage)
            throw new DependencyUnavailableException(
                "AI_IMAGE_UNSUPPORTED",
                "O provedor de IA em uso não aceita imagem. Descreva o pedido em texto.");

        var sanitized = image is null ? null : await SanitizeImageAsync(image, cancellationToken);
        var run = AiDraftRun.Start(aiClient.ModelName, configured.PromptVersion, clock.GetUtcNow());
        db.AiDraftRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await aiClient.GenerateAsync(
                new(text, sanitized?.Bytes, sanitized?.MediaType),
                cancellationToken);
            ValidateResult(result);

            var items = result.Items
                .GroupBy(x => x.ProductId)
                .Select(group =>
                {
                    var first = group.First();
                    return new AiDraftItem(
                        first.ProductId,
                        first.Code,
                        first.Description,
                        checked(group.Sum(x => x.Quantity)),
                        first.Availability);
                })
                .ToList();

            var estimatedCost =
                result.InputTokens / 1_000_000m * configured.EstimatedUsdPerMillionInputTokens +
                result.OutputTokens / 1_000_000m * configured.EstimatedUsdPerMillionOutputTokens;
            run.Complete(
                JsonSerializer.Serialize(result.ToolNames.Distinct()),
                result.InputTokens,
                result.OutputTokens,
                estimatedCost,
                stopwatch.ElapsedMilliseconds,
                clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return new(run.Id, items, result.UnresolvedItems, result.Warnings, result.Steps);
        }
        catch (AppException)
        {
            run.Fail("AI_VALIDATION_FAILED", "[]", stopwatch.ElapsedMilliseconds, clock.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            run.Fail("AI_UNAVAILABLE", "[]", stopwatch.ElapsedMilliseconds, clock.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("AI draft run {RunId} failed with {ExceptionType}", run.Id, ex.GetType().Name);
            throw new DependencyUnavailableException(
                "AI_UNAVAILABLE",
                "O copiloto está temporariamente indisponível. Continue pelo preenchimento manual.");
        }
    }

    private static void ValidateResult(AiDraftModelResult result)
    {
        if (result.Items.Count > 20)
            throw new DomainValidationException("A sugestão excedeu o limite de 20 produtos.");
        foreach (var item in result.Items)
        {
            if (item.ProductId == Guid.Empty || item.Quantity <= 0 || item.Quantity > 1_000_000)
                throw new DomainValidationException("A IA retornou produto ou quantidade inválida.");
            if (!result.DiscoveredProductIds.Contains(item.ProductId))
                throw new DomainValidationException("A IA retornou um produto que não foi descoberto pelo catálogo.");
        }
    }

    private static async Task<SanitizedImage> SanitizeImageAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > 5 * 1024 * 1024)
            throw new DomainValidationException("A imagem deve ter no máximo 5 MB.");

        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var sourceBytes = buffer.ToArray();
        using var data = SKData.CreateCopy(sourceBytes);
        using var codec = SKCodec.Create(data)
            ?? throw new DomainValidationException("A imagem enviada é inválida.");
        if ((long)codec.Info.Width * codec.Info.Height > 12_000_000)
            throw new DomainValidationException("A imagem deve ter no máximo 12 megapixels.");
        if (codec.FrameCount != 1)
            throw new DomainValidationException("Imagens animadas não são aceitas.");
        var (format, mediaType) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => (SKEncodedImageFormat.Jpeg, "image/jpeg"),
            SKEncodedImageFormat.Png => (SKEncodedImageFormat.Png, "image/png"),
            SKEncodedImageFormat.Webp => (SKEncodedImageFormat.Webp, "image/webp"),
            _ => throw new DomainValidationException("Use uma imagem JPEG, PNG ou WebP válida.")
        };
        using var bitmap = SKBitmap.Decode(sourceBytes)
            ?? throw new DomainValidationException("A imagem enviada é inválida.");
        using var safeImage = SKImage.FromBitmap(bitmap);
        using var encoded = safeImage.Encode(format, format == SKEncodedImageFormat.Png ? 100 : 85)
            ?? throw new DomainValidationException("Não foi possível processar a imagem.");
        return new(encoded.ToArray(), mediaType);
    }

    private sealed record SanitizedImage(byte[] Bytes, string MediaType);
}
