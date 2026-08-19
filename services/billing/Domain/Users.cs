using System.Security.Cryptography;

namespace Billing.Api.Domain;

/// <summary>
/// Operador da aplicação. A identidade vive no Billing, que já é dono das
/// notas; o Inventory não ganha tabela de usuários — recebe o operador atuante
/// propagado em cabeçalho próprio, separado do token entre serviços.
/// </summary>
public sealed class AppUser
{
    // PBKDF2 com SHA-256: suficiente e sem dependência externa.
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    private AppUser()
    {
    }

    private AppUser(Guid id, string userName, string displayName, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        UserName = userName;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static AppUser Create(
        string userName,
        string displayName,
        string password,
        DateTimeOffset createdAt)
    {
        var normalized = NormalizeUserName(userName);
        if (normalized.Length is < 3 or > 64)
        {
            throw new ArgumentException("User name must contain between 3 and 64 characters.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            throw new ArgumentException("Password must contain at least 6 characters.", nameof(password));
        }

        return new AppUser(
            Guid.NewGuid(),
            normalized,
            displayName.Trim(),
            HashPassword(password),
            createdAt);
    }

    public static string NormalizeUserName(string userName) =>
        (userName ?? string.Empty).Trim().ToLowerInvariant();

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool VerifyPassword(string password)
    {
        var parts = PasswordHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            // Tempo constante: não vaza informação por diferença de duração.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Sessão emitida no login. O token é opaco e guardado por hash.</summary>
public sealed class UserSession
{
    private UserSession()
    {
    }

    private UserSession(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public AppUser? User { get; private set; }

    public static (UserSession Session, string Token) Issue(
        Guid userId,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

        var session = new UserSession(Guid.NewGuid(), userId, HashToken(token), now, now.Add(lifetime));
        return (session, token);
    }

    /// <summary>O token nunca é gravado em claro: só o digest.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public bool IsValidAt(DateTimeOffset now) => now < ExpiresAt;
}
