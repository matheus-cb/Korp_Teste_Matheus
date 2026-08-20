using Billing.Api.Application;
using Billing.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Api;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            await WriteProblemAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Errors);
        }
        catch (UnauthorizedDomainException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", ex.Message);
        }
        catch (BadHttpRequestException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "CONCURRENT_MODIFICATION", "O recurso foi alterado por outra operação. Tente novamente.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Quem desistiu foi o outro lado: a pessoa fechou a aba, ou um proxy
            // cortou por tempo. Nao e falha nossa, e tratar como 500 mascarava a
            // causa -- o timeout do proxy aparecia como "erro inesperado".
            logger.LogInformation("Requisicao cancelada pelo cliente em {Path}.", context.Request.Path);
            if (!context.Response.HasStarted)
            {
                // 499 e a convencao do nginx para cliente que desistiu.
                context.Response.StatusCode = 499;
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Operacao cancelada por tempo em {Path}.", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status504GatewayTimeout,
                "REQUEST_TIMEOUT",
                "A operação demorou mais que o limite. Tente de novo, ou divida o pedido em partes menores.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled request failure for trace {TraceId}", context.TraceIdentifier);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Ocorreu um erro inesperado.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = message,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (errors is not null) problem.Extensions["errors"] = errors;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    }
}
