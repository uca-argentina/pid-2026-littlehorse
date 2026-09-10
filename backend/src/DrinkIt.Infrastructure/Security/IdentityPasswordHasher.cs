using DrinkIt.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DrinkIt.Infrastructure.Security;

/// <summary>
/// Adapts ASP.NET Core Identity's hasher, which is PBKDF2-HMAC-SHA512 with a
/// per-password salt. Named after the technology so that a future BCrypt or
/// Argon2 adapter can sit beside it instead of replacing it in place.
/// </summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// What OWASP asks for on PBKDF2-HMAC-SHA512, the PRF below.
    /// Identity has a default of 100,000. Every login attempt pays this.
    /// </summary>
    private const int IterationCount = 210_000;

    private static readonly PasswordHasher<HashOnly> Hasher =
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));

    private static readonly HashOnly Unused = new();

    public string Hash(string password) => Hasher.HashPassword(Unused, password);

    public bool Verify(string password, string hash) =>
        // SuccessRehashNeeded means the stored hash used older parameters. It is
        // still a correct password; upgrading the stored hash is a separate job.
        Hasher.VerifyHashedPassword(Unused, hash, password) is not PasswordVerificationResult.Failed;

    /// <summary>Identity's hasher is generic over the user type but never reads it.</summary>
    private sealed class HashOnly;
}
