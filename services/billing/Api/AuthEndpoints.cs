using System.ComponentModel.DataAnnotations;
using Billing.Api.Application;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Api;

public sealed record SignInRequest(
    [Required, StringLength(64, MinimumLength = 3)] string UserName,
    [Required, StringLength(128, MinimumLength = 6)] string Password);

public sealed record SignInResponse(
    string Token,
    string UserName,
    string DisplayName,
    DateTimeOffset ExpiresAt);

public sealed record CurrentUserResponse(string UserName, string DisplayName);

public static class AuthEndpoints
{
    public const string TokenHeader = "Authorization";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", SignInAsync)
            .AllowAnonymous()
            .Produces<SignInResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", SignOutAsync).Produces(StatusCodes.Status204NoContent);
        group.MapGet("/me", GetCurrentUser).Produces<CurrentUserResponse>();

        return endpoints;
    }

    private static async Task<IResult> SignInAsync(
        [FromBody] SignInRequest request,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.SignInAsync(request.UserName, request.Password, cancellationToken);
        return Results.Ok(
            new SignInResponse(result.Token, result.UserName, result.DisplayName, result.ExpiresAt));
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext context,
        AuthService auth,
        CancellationToken cancellationToken)
    {
        var token = ReadToken(context);
        if (!string.IsNullOrEmpty(token))
        {
            await auth.SignOutAsync(token, cancellationToken);
        }

        return Results.NoContent();
    }

    private static IResult GetCurrentUser(HttpContext context)
    {
        var user = context.GetCurrentUser();
        return user is null
            ? Results.Unauthorized()
            : Results.Ok(new CurrentUserResponse(user.UserName, user.DisplayName));
    }

    public static string? ReadToken(HttpContext context)
    {
        var values = context.Request.Headers[TokenHeader];
        if (values.Count != 1) return null;

        var value = values[0];
        const string prefix = "Bearer ";
        return value is not null && value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..].Trim()
            : null;
    }
}
