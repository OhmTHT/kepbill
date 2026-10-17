using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Domain.Entities;

/// <summary>
/// A person who can sign in with their ชื่อผู้ใช้งาน and password. Admins see every company; members only the
/// companies linked to them through <see cref="UserCompany"/>.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>ชื่อผู้ใช้งาน: shown in the app and used to sign in.</summary>
    public string Name { get; set; } = "";
    /// <summary><see cref="Name"/> lower-cased with whitespace collapsed; unique, so sign-in by name is unambiguous.</summary>
    public string NormalizedName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.User;
    /// <summary>Deactivated users cannot sign in and their open sessions end on the next request.</summary>
    public bool Active { get; set; } = true;
    /// <summary>Copied into the session cookie; changing it (password change, deactivation) ends every other session.</summary>
    public string SecurityStamp { get; set; } = NewStamp();
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string NewStamp() => Guid.NewGuid().ToString("N");
}

/// <summary>A company a member may work with. Not used for admins, who can open every company.</summary>
public class UserCompany
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
}
