using Microsoft.AspNetCore.Identity;

namespace ReceiptReader.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's hasher (PBKDF2, salted, versioned format) without the rest of Identity.</summary>
internal sealed class PasswordHasher : Application.Abstractions.IPasswordHasher
{
    private static readonly PasswordHasher<object> Inner = new();
    private static readonly object NoUser = new();

    public string Hash(string password) => Inner.HashPassword(NoUser, password);

    public bool Verify(string hash, string password) =>
        Inner.VerifyHashedPassword(NoUser, hash, password) != PasswordVerificationResult.Failed;
}
