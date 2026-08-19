using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Application;

public sealed record SignInResult(string Token, string UserName, string DisplayName, DateTimeOffset ExpiresAt);

/// <summary>Operador autenticado da requisição em curso.</summary>
public sealed record CurrentUser(Guid Id, string UserName, string DisplayName);

/// <summary>
/// Login, sessão e resolução do operador atual. Sessão é opaca e guardada por
/// hash — nem o banco nem os logs veem o token em claro.
/// </summary>
public sealed class AuthService(BillingDbContext database, TimeProvider timeProvider)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    public async Task<SignInResult> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = AppUser.NormalizeUserName(userName);
        var user = await database.Users.SingleOrDefaultAsync(
            candidate => candidate.UserName == normalized,
            cancellationToken);

        // Mensagem única para usuário inexistente e senha errada: não confirma
        // a existência de contas para quem está tentando adivinhar.
        if (user is null || !user.VerifyPassword(password ?? string.Empty))
        {
            throw new UnauthorizedDomainException("Usuário ou senha inválidos.");
        }

        var now = timeProvider.GetUtcNow();
        var (session, token) = UserSession.Issue(user.Id, now, SessionLifetime);
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);

        return new SignInResult(token, user.UserName, user.DisplayName, session.ExpiresAt);
    }

    public async Task SignOutAsync(string token, CancellationToken cancellationToken)
    {
        var hash = UserSession.HashToken(token);
        var session = await database.Sessions.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == hash,
            cancellationToken);
        if (session is null) return;

        database.Sessions.Remove(session);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<CurrentUser?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = UserSession.HashToken(token);
        var session = await database.Sessions
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (session?.User is null || !session.IsValidAt(timeProvider.GetUtcNow()))
        {
            return null;
        }

        return new CurrentUser(session.User.Id, session.User.UserName, session.User.DisplayName);
    }
}

public sealed class UnauthorizedDomainException(string message) : Exception(message);
