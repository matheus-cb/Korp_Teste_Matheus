using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Errors;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string ProductCodeAlreadyExists = "PRODUCT_CODE_ALREADY_EXISTS";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyInvalid = "IDEMPOTENCY_KEY_INVALID";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string StockDebitNotFound = "STOCK_DEBIT_NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
}

public sealed class InventoryApiException : Exception
{
    public InventoryApiException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }

    public static InventoryApiException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    public static InventoryApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    public static InventoryApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}

public sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var apiException = exception as InventoryApiException;
        var statusCode = apiException?.StatusCode ?? StatusCodes.Status500InternalServerError;
        var code = apiException?.Code ?? ErrorCodes.InternalError;
        var message = apiException?.Message ?? "An unexpected error occurred.";

        if (statusCode >= 500)
        {
            LogUnhandledError(exception, httpContext.TraceIdentifier);
        }
        else
        {
            LogRejectedRequest(code, httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                StatusCodes.Status400BadRequest => "Invalid request.",
                StatusCodes.Status404NotFound => "Resource not found.",
                StatusCodes.Status409Conflict => "Request conflicts with current state.",
                _ => "Unexpected server error."
            },
            Detail = message,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled inventory API error. TraceId: {TraceId}")]
    private partial void LogUnhandledError(Exception exception, string traceId);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Inventory API request rejected with {Code}. TraceId: {TraceId}")]
    private partial void LogRejectedRequest(string code, string traceId);
}
