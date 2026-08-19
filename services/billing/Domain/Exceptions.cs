namespace Billing.Api.Domain;

public class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}

public sealed class DomainValidationException(string message)
    : AppException(StatusCodes.Status400BadRequest, "VALIDATION_ERROR", message);

public sealed class ResourceNotFoundException(string code, string message)
    : AppException(StatusCodes.Status404NotFound, code, message);

public sealed class ConflictException(string code, string message)
    : AppException(StatusCodes.Status409Conflict, code, message);

public sealed class DependencyUnavailableException(string code, string message)
    : AppException(StatusCodes.Status503ServiceUnavailable, code, message);
