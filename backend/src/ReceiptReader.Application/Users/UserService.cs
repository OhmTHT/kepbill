using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Users;

/// <param name="CompanyIds">Companies the user may open; empty for super admins, who can open every company.</param>
public sealed record UserDto(
    Guid Id, string Name, UserRole Role, bool Active, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> CompanyIds);

/// <summary>What the session cookie carries; <see cref="SecurityStamp"/> must still match for the session to stay valid.</summary>
public sealed record SessionUser(Guid Id, string Name, UserRole Role, string SecurityStamp);

public sealed record AuthStatusDto(bool SetupRequired, UserDto? User);

public sealed record SetupInput(string Name, string Password);

/// <param name="Name">ชื่อผู้ใช้งาน.</param>
public sealed record LoginInput(string Name, string Password, bool Remember = false);

public sealed record ChangePasswordInput(string CurrentPassword, string NewPassword);

public sealed record UserInput(string Name, string Password, UserRole Role = UserRole.User, IReadOnlyList<Guid>? CompanyIds = null);

/// <summary>The signed-in user managing others.</summary>
public sealed record Actor(Guid Id, UserRole Role);

public sealed record UserUpdate(string Name, UserRole Role, bool Active, IReadOnlyList<Guid>? CompanyIds = null);

public sealed record ResetPasswordInput(string Password);

/// <summary>Sign-in, the first-run admin, and user management.</summary>
public sealed partial class UserService(IAppDbContext db, IPasswordHasher hasher, IClock clock)
{
    internal const int MinPasswordLength = 8;
    internal const int MaxPasswordLength = 128;
    internal const int MaxNameLength = 100;
    internal const int MaxFailedAttempts = 5;
    internal static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Hashed once so an unknown name costs as much time as a wrong password.</summary>
    private static string? dummyHash;

    public async Task<bool> SetupRequiredAsync(CancellationToken ct) => !await db.Users.AnyAsync(ct);

    public async Task<AuthStatusDto> StatusAsync(Guid? userId, CancellationToken ct)
    {
        var setupRequired = await SetupRequiredAsync(ct);
        var user = userId is { } id ? (await QueryDtos(u => u.Id == id && u.Active).ToListAsync(ct)).FirstOrDefault() : null;
        return new AuthStatusDto(setupRequired, user);
    }

    /// <summary>Creates the first super admin. Only allowed while there are no users at all.</summary>
    public async Task<SessionUser> SetupAsync(SetupInput input, CancellationToken ct)
    {
        if (!await SetupRequiredAsync(ct)) throw new ConflictException("ตั้งค่าผู้ดูแลระบบไปแล้ว — เข้าสู่ระบบด้วยบัญชีที่มีอยู่");
        var user = NewUser(input.Name, input.Password, UserRole.SuperAdmin);
        user.LastLoginAt = clock.Now;
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return ToSession(user);
    }

    public async Task<SessionUser> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var key = NameKey(input.Name);
        var password = input.Password ?? "";
        var user = key.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.NormalizedName == key, ct);
        if (user is null || !user.Active)
        {
            hasher.Verify(dummyHash ??= hasher.Hash(Guid.NewGuid().ToString()), password);
            throw new BadRequestException(WrongCredentials);
        }

        var now = clock.Now;
        if (user.LockoutEnd > now)
        {
            var minutes = (int)Math.Ceiling((user.LockoutEnd.Value - now).TotalMinutes);
            throw new BadRequestException($"ใส่รหัสผ่านผิดหลายครั้ง บัญชีถูกล็อกชั่วคราว — ลองใหม่ในอีก {minutes} นาที");
        }

        if (!hasher.Verify(user.PasswordHash, password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.FailedLoginCount = 0;
                user.LockoutEnd = now + LockoutDuration;
            }
            await db.SaveChangesAsync(ct);
            throw new BadRequestException(WrongCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        return ToSession(user);
    }

    /// <summary>The session's user if it still exists and is active; null ends the session.</summary>
    public async Task<SessionUser?> FindSessionAsync(Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Id == id && u.Active)
            .Select(u => new SessionUser(u.Id, u.Name, u.Role, u.SecurityStamp))
            .FirstOrDefaultAsync(ct);

    /// <summary>Changes the password and ends the user's other sessions; returns the new session.</summary>
    public async Task<SessionUser> ChangePasswordAsync(Guid userId, ChangePasswordInput input, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Active, ct)
                   ?? throw new NotFoundException("ไม่พบผู้ใช้");
        if (!hasher.Verify(user.PasswordHash, input.CurrentPassword ?? ""))
            throw new BadRequestException("รหัสผ่านปัจจุบันไม่ถูกต้อง");
        SetPassword(user, input.NewPassword);
        await db.SaveChangesAsync(ct);
        return ToSession(user);
    }

    // ---------------- user management ----------------

    /// <summary>Super admins see everyone; admins see the approvers and users of their companies.</summary>
    public async Task<IReadOnlyList<UserDto>> ListAsync(Actor actor, CancellationToken ct)
    {
        var scope = await ScopeAsync(actor, ct);
        if (scope is null) return await QueryDtos(_ => true).ToListAsync(ct);
        var roles = RoleRules.Assignable(actor.Role).ToList();
        return await QueryDtos(u => u.Id != actor.Id && roles.Contains(u.Role)
                                    && db.UserCompanies.Any(uc => uc.UserId == u.Id && scope.Contains(uc.CompanyId)), scope)
            .ToListAsync(ct);
    }

    public async Task<UserDto> CreateAsync(UserInput input, Actor actor, CancellationToken ct)
    {
        EnsureAssignable(actor, input.Role);
        var scope = await ScopeAsync(actor, ct);
        var companies = (input.CompanyIds ?? []).ToHashSet();
        if (scope is not null)
        {
            if (companies.Count == 0) throw new BadRequestException("เลือกบริษัทอย่างน้อย 1 บริษัท");
            if (!companies.All(scope.Contains)) throw new ForbiddenException("เพิ่มผู้ใช้ได้เฉพาะในบริษัทที่คุณดูแล");
        }

        var user = NewUser(input.Name, input.Password, input.Role);
        await EnsureNameFreeAsync(user, ct);
        db.Users.Add(user);
        await SetCompaniesAsync(user, companies, ct);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(user.Id, scope, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UserUpdate input, Actor actor, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("ไม่พบผู้ใช้");
        var scope = await ScopeAsync(actor, ct);
        await EnsureManageableAsync(user, actor, scope, ct);
        EnsureAssignable(actor, input.Role);

        if (id == actor.Id && (!input.Active || input.Role != actor.Role))
            throw new BadRequestException("ลดสิทธิ์หรือปิดบัญชีของตัวเองไม่ได้ — ให้ผู้ดูแลคนอื่นทำแทน");

        var stillSuper = input.Active && input.Role == UserRole.SuperAdmin;
        if (!stillSuper && user is { Active: true, Role: UserRole.SuperAdmin } &&
            !await db.Users.AnyAsync(u => u.Id != id && u.Active && u.Role == UserRole.SuperAdmin, ct))
            throw new BadRequestException("ต้องมีผู้ดูแลระบบที่ใช้งานอยู่อย่างน้อย 1 คน");

        var requested = (input.CompanyIds ?? []).ToHashSet();
        if (scope is not null && !requested.All(scope.Contains))
            throw new ForbiddenException("ผูกได้เฉพาะบริษัทที่คุณดูแล");

        SetName(user, input.Name);
        await EnsureNameFreeAsync(user, ct);
        user.Role = input.Role;
        if (user.Active && !input.Active) user.SecurityStamp = User.NewStamp();
        user.Active = input.Active;
        user.UpdatedAt = clock.Now;

        // A company admin only touches memberships inside their own companies.
        if (scope is not null)
        {
            var current = await db.UserCompanies.Where(uc => uc.UserId == id).Select(uc => uc.CompanyId).ToListAsync(ct);
            requested = RoleRules.MergeCompanies(current, requested, scope);
        }
        await SetCompaniesAsync(user, requested, ct);
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(id, scope, ct);
    }

    /// <summary>Sets a new password chosen by an admin, unlocks the account and ends its sessions.</summary>
    public async Task ResetPasswordAsync(Guid id, ResetPasswordInput input, Actor actor, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("ไม่พบผู้ใช้");
        await EnsureManageableAsync(user, actor, await ScopeAsync(actor, ct), ct);
        SetPassword(user, input.Password);
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- helpers ----------------

    private const string WrongCredentials = "ชื่อผู้ใช้งานหรือรหัสผ่านไม่ถูกต้อง";

    /// <summary>
    /// Sorted by name here: EF cannot order by a DTO that carries a nested list. With a <paramref name="scope"/>,
    /// only those companies are listed (a company admin doesn't see the user's other companies).
    /// </summary>
    private IQueryable<UserDto> QueryDtos(System.Linq.Expressions.Expression<Func<User, bool>> filter, IReadOnlyCollection<Guid>? scope = null) =>
        db.Users.AsNoTracking().Where(filter).OrderBy(u => u.Name).Select(u => new UserDto(
            u.Id, u.Name, u.Role, u.Active, u.LastLoginAt, u.CreatedAt,
            db.UserCompanies.Where(uc => uc.UserId == u.Id && (scope == null || scope.Contains(uc.CompanyId)))
                .Select(uc => uc.CompanyId).ToList()));

    private async Task<UserDto> GetDtoAsync(Guid id, CancellationToken ct) => await GetDtoAsync(id, null, ct);

    private async Task<UserDto> GetDtoAsync(Guid id, IReadOnlyCollection<Guid>? scope, CancellationToken ct) =>
        (await QueryDtos(u => u.Id == id, scope).ToListAsync(ct)).Single();

    /// <summary>The companies a non-super-admin actor manages; null = every company.</summary>
    private async Task<IReadOnlyCollection<Guid>?> ScopeAsync(Actor actor, CancellationToken ct) =>
        RoleRules.SeesAllCompanies(actor.Role)
            ? null
            : await db.UserCompanies.Where(uc => uc.UserId == actor.Id).Select(uc => uc.CompanyId).ToListAsync(ct);

    private static void EnsureAssignable(Actor actor, UserRole role)
    {
        if (!RoleRules.Assignable(actor.Role).Contains(role))
            throw new ForbiddenException("คุณกำหนดบทบาทนี้ให้ผู้ใช้ไม่ได้");
    }

    /// <summary>Super admins manage anyone; admins only approvers and users who share one of their companies.</summary>
    private async Task EnsureManageableAsync(User target, Actor actor, IReadOnlyCollection<Guid>? scope, CancellationToken ct)
    {
        if (scope is null) return;
        var inScope = await db.UserCompanies.AnyAsync(uc => uc.UserId == target.Id && scope.Contains(uc.CompanyId), ct);
        if (target.Id == actor.Id || !RoleRules.CanManage(actor.Role, target.Role) || !inScope)
            throw new ForbiddenException("คุณจัดการผู้ใช้คนนี้ไม่ได้");
    }

    private static SessionUser ToSession(User u) => new(u.Id, u.Name, u.Role, u.SecurityStamp);

    /// <summary>Replaces the user's companies. Super admins see every company, so they keep no list.</summary>
    private async Task SetCompaniesAsync(User user, IReadOnlyCollection<Guid> companyIds, CancellationToken ct)
    {
        var wanted = RoleRules.SeesAllCompanies(user.Role) ? new HashSet<Guid>() : companyIds.ToHashSet();
        if (wanted.Count > 0 && await db.Companies.CountAsync(c => wanted.Contains(c.Id), ct) != wanted.Count)
            throw new BadRequestException("มีบริษัทที่เลือกไม่อยู่ในระบบแล้ว — โหลดหน้าใหม่แล้วลองอีกครั้ง");

        var current = await db.UserCompanies.Where(uc => uc.UserId == user.Id).ToListAsync(ct);
        db.UserCompanies.RemoveRange(current.Where(uc => !wanted.Contains(uc.CompanyId)));
        foreach (var companyId in wanted.Where(w => current.All(uc => uc.CompanyId != w)))
            db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = companyId });
    }

    private async Task EnsureNameFreeAsync(User user, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.NormalizedName == user.NormalizedName && u.Id != user.Id, ct))
            throw new ConflictException($"ชื่อผู้ใช้งาน “{user.Name}” ถูกใช้แล้ว");
    }

    private User NewUser(string name, string password, UserRole role)
    {
        var user = new User { Role = role, CreatedAt = clock.Now };
        SetName(user, name);
        SetPassword(user, password);
        return user;
    }

    private static void SetName(User user, string? name)
    {
        user.Name = RequireName(name);
        user.NormalizedName = NameKey(user.Name);
    }

    private void SetPassword(User user, string? password)
    {
        user.PasswordHash = hasher.Hash(RequirePassword(password));
        user.SecurityStamp = User.NewStamp();
        user.UpdatedAt = clock.Now;
    }

    /// <summary>The name as it will be shown: trimmed, runs of whitespace collapsed to one space.</summary>
    internal static string CleanName(string? name) =>
        Whitespace().Replace((name ?? "").Normalize(NormalizationForm.FormC), " ").Trim();

    /// <summary>What names are compared by (sign-in and uniqueness): <see cref="CleanName"/>, lower-cased.</summary>
    internal static string NameKey(string? name) => CleanName(name).ToLowerInvariant();

    internal static string RequireName(string? name)
    {
        var n = CleanName(name);
        if (n.Length == 0) throw new BadRequestException("กรุณากรอกชื่อผู้ใช้งาน");
        if (n.Length > MaxNameLength) throw new BadRequestException($"ชื่อผู้ใช้งานยาวเกิน {MaxNameLength} ตัวอักษร");
        return n;
    }

    internal static string RequirePassword(string? password)
    {
        var p = password ?? "";
        if (p.Length < MinPasswordLength) throw new BadRequestException($"รหัสผ่านต้องยาวอย่างน้อย {MinPasswordLength} ตัวอักษร");
        if (p.Length > MaxPasswordLength) throw new BadRequestException($"รหัสผ่านยาวเกิน {MaxPasswordLength} ตัวอักษร");
        return p;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
