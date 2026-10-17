using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using ReceiptReader.Api.Auth;
using ReceiptReader.Application.Users;

namespace ReceiptReader.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapGet("/status", (ClaimsPrincipal user, UserService svc, CancellationToken ct) => svc.StatusAsync(user.UserId(), ct))
            .AllowAnonymous()
            .WithSummary("Whether the first admin still has to be created, and who is signed in (null when nobody)");

        auth.MapPost("/setup", async ([FromBody] SetupInput input, HttpContext http, UserService svc, CancellationToken ct) =>
            {
                // Until the first admin exists the site has no owner: only someone at the server machine may claim it,
                // never a visitor arriving through the public tunnel (Cloudflare always sets CF-Connecting-IP).
                if (http.Request.Headers.ContainsKey("CF-Connecting-IP"))
                    return Results.Problem(
                        title: "Forbidden",
                        detail: "ตั้งค่าผู้ดูแลระบบครั้งแรกได้จากเครื่องเซิร์ฟเวอร์เท่านั้น — เปิด http://localhost:8080/setup บนเครื่องนั้น",
                        statusCode: StatusCodes.Status403Forbidden);

                var user = await svc.SetupAsync(input, ct);
                await http.SignInAsync(user, persistent: false);
                return Results.Ok(await svc.StatusAsync(user.Id, ct));
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.LoginRateLimit)
            .WithSummary("Create the first admin (only while there are no users, only from the server machine) and sign in");

        auth.MapPost("/login", async ([FromBody] LoginInput input, HttpContext http, UserService svc, CancellationToken ct) =>
            {
                var user = await svc.LoginAsync(input, ct);
                await http.SignInAsync(user, input.Remember);
                return await svc.StatusAsync(user.Id, ct);
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthSetup.LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapPost("/change-password",
                async ([FromBody] ChangePasswordInput input, HttpContext http, UserService svc, CancellationToken ct) =>
                {
                    var persistent = (await http.AuthenticateAsync()).Properties?.IsPersistent ?? false;
                    var user = await svc.ChangePasswordAsync(http.User.UserId()!.Value, input, ct);
                    // The new security stamp ends every other session; keep this one by issuing a fresh cookie.
                    await http.SignInAsync(user, persistent);
                    return Results.NoContent();
                })
            .RequireRateLimiting(AuthSetup.LoginRateLimit);

        // ---- user management: super admins (everyone), admins (approvers/users of their companies) ----
        var users = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(AuthSetup.ManagerPolicy);
        static Actor ActorOf(ClaimsPrincipal user) => new(user.UserId()!.Value, user.Role());

        users.MapGet("/", (ClaimsPrincipal user, UserService svc, CancellationToken ct) => svc.ListAsync(ActorOf(user), ct));
        users.MapPost("/", ([FromBody] UserInput input, ClaimsPrincipal user, UserService svc, CancellationToken ct) =>
            svc.CreateAsync(input, ActorOf(user), ct));
        users.MapPut("/{id:guid}", (Guid id, [FromBody] UserUpdate input, ClaimsPrincipal user, UserService svc, CancellationToken ct) =>
            svc.UpdateAsync(id, input, ActorOf(user), ct));
        users.MapPost("/{id:guid}/reset-password",
            async (Guid id, [FromBody] ResetPasswordInput input, ClaimsPrincipal user, UserService svc, CancellationToken ct) =>
            {
                await svc.ResetPasswordAsync(id, input, ActorOf(user), ct);
                return Results.NoContent();
            });

        return app;
    }
}
