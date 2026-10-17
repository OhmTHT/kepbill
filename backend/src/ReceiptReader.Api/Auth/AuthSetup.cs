using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using ReceiptReader.Application.Users;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Api.Auth;

/// <summary>
/// Cookie sessions for the SPA. Every endpoint requires a signed-in user unless it opts out with AllowAnonymous;
/// the cookie is re-checked against the database on each request so deactivation and password changes apply at once.
/// </summary>
public static class AuthSetup
{
    /// <summary>The whole system: adding and removing companies, Training AI.</summary>
    public const string SuperAdminPolicy = "SuperAdmin";
    /// <summary>Company settings and users: admins (their companies) and super admins.</summary>
    public const string ManagerPolicy = "Manager";
    /// <summary>Approving, payments, periods, journal and outstanding: approvers and up.</summary>
    public const string ApproverPolicy = "Approver";
    public const string LoginRateLimit = "login";
    private const string StampClaim = "stamp";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config)
    {
        // Keys that encrypt the session cookie. Persisted so sessions survive restarts and container rebuilds.
        services.AddDataProtection()
            .SetApplicationName("ReceiptReader")
            .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeysPath"] ?? "data/keys"));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "rr_session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // https behind the tunnel (X-Forwarded-Proto)
                o.ExpireTimeSpan = TimeSpan.FromDays(7);
                o.SlidingExpiration = true;
                // This is an API: answer with a status code instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => SetStatus(ctx.Response, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = ctx => SetStatus(ctx.Response, StatusCodes.Status403Forbidden);
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(SuperAdminPolicy, p => p.RequireRole(nameof(UserRole.SuperAdmin)))
            .AddPolicy(ManagerPolicy, p => p.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)))
            .AddPolicy(ApproverPolicy, p => p.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Admin), nameof(UserRole.Approver)));

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, _) => await Results.Problem(
                title: "Too many requests",
                detail: "พยายามเข้าสู่ระบบบ่อยเกินไป — รอสักครู่แล้วลองใหม่",
                statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(ctx.HttpContext);
            o.AddPolicy(LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(ClientIp(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The signed-in user's role (User when the claim is missing or unknown).</summary>
    public static UserRole Role(this ClaimsPrincipal principal) =>
        Enum.TryParse<UserRole>(principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : UserRole.User;

    public static Task SignInAsync(this HttpContext http, SessionUser user, bool persistent) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = persistent });

    private static ClaimsPrincipal CreatePrincipal(SessionUser u) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
        new Claim(ClaimTypes.Name, u.Name),
        new Claim(ClaimTypes.Role, u.Role.ToString()),
        new Claim(StampClaim, u.SecurityStamp),
    ], CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role));

    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        var user = principal?.UserId() is { } id
            ? await ctx.HttpContext.RequestServices.GetRequiredService<UserService>().FindSessionAsync(id, ctx.HttpContext.RequestAborted)
            : null;

        if (principal is null || user is null || user.SecurityStamp != principal.FindFirstValue(StampClaim))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        // Name or role changed by an admin: refresh the cookie instead of ending the session.
        if (user.Name != principal.Identity?.Name || !principal.IsInRole(user.Role.ToString()))
        {
            ctx.ReplacePrincipal(CreatePrincipal(user));
            ctx.ShouldRenew = true;
        }
    }

    /// <summary>The visitor's address: Cloudflare's header when behind the tunnel, else the TCP peer.</summary>
    private static string ClientIp(HttpContext http) =>
        http.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
        ?? http.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    private static Task SetStatus(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }
}
