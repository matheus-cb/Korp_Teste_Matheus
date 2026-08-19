using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;

namespace Billing.Api.Api;

/// <summary>Importação de notas por CSV (multipart).</summary>
public static class ImportEndpoints
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/invoices/import", ImportAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ImportResultResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .WithTags("Import");

        return endpoints;
    }

    private static async Task<IResult> ImportAsync(
        HttpRequest request,
        InvoiceImportService importer,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw new DomainValidationException("Envie o arquivo como multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? (form.Files.Count > 0 ? form.Files[0] : null);

        if (file is null || file.Length == 0)
        {
            throw new DomainValidationException("Nenhum arquivo foi enviado.");
        }

        if (file.Length > MaxFileBytes)
        {
            throw new DomainValidationException("O arquivo excede o limite de 2 MB.");
        }

        // Sem validar extensão apenas: o parser trata o conteúdo e reporta
        // linha a linha, então um arquivo errado vira erro legível, não 500.
        var closeAfterImport = string.Equals(
            form["close"].ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);

        await using var stream = file.OpenReadStream();
        var result = await importer.ImportAsync(stream, file.FileName, closeAfterImport, cancellationToken);
        return Results.Ok(result);
    }
}
