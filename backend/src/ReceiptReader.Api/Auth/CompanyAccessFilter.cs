using System.Security.Claims;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Application.Users;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Api.Auth;

/// <summary>
/// Stops members from touching companies that are not linked to them. Applied to route groups: it finds the company
/// a request is about from the <c>{id}</c> route value (a company or a document, depending on <see cref="RouteId"/>),
/// a <c>companyId</c> query value, or a request body that names a company. Unknown ids pass through so the
/// endpoint answers 404 as before. Super admins are never restricted.
/// </summary>
public sealed class CompanyAccessFilter(CompanyAccessFilter.RouteId routeId) : IEndpointFilter
{
    public enum RouteId
    {
        /// <summary>/api/companies/{id}</summary>
        Company,
        /// <summary>/api/receipts/{id}</summary>
        Receipt,
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var user = http.User;
        if (user.IsInRole(nameof(UserRole.SuperAdmin)) || user.UserId() is not { } userId) return await next(ctx);

        var access = http.RequestServices.GetRequiredService<CompanyAccess>();
        var ct = http.RequestAborted;

        foreach (var companyId in await CompaniesInRequestAsync(ctx, access, ct))
            if (!await access.CanOpenCompanyAsync(userId, seesAll: false, companyId, ct))
                return Results.Problem(
                    title: "Forbidden",
                    detail: "คุณไม่มีสิทธิ์เข้าถึงบริษัทนี้ — ติดต่อผู้ดูแลระบบ",
                    statusCode: StatusCodes.Status403Forbidden);

        return await next(ctx);
    }

    private async Task<List<Guid>> CompaniesInRequestAsync(EndpointFilterInvocationContext ctx, CompanyAccess access, CancellationToken ct)
    {
        var http = ctx.HttpContext;
        var found = new List<Guid>();

        if (Guid.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id))
        {
            if (routeId == RouteId.Company) found.Add(id);
            else if (await access.CompanyOfReceiptAsync(id, ct) is { } receiptCompany) found.Add(receiptCompany);
        }

        if (Guid.TryParse(http.Request.Query["companyId"], out var queryCompany)) found.Add(queryCompany);

        foreach (var arg in ctx.Arguments)
            if (arg is ManualReceiptRequest manual) found.Add(manual.CompanyId);

        return found;
    }
}

public static class CompanyAccessFilterExtensions
{
    public static RouteGroupBuilder RequireCompanyAccess(this RouteGroupBuilder group, CompanyAccessFilter.RouteId routeId)
    {
        group.AddEndpointFilter(new CompanyAccessFilter(routeId));
        return group;
    }

    /// <summary>The user's companies, or null for a super admin.</summary>
    public static Task<IReadOnlyCollection<Guid>?> AllowedCompaniesAsync(this CompanyAccess access, ClaimsPrincipal user, CancellationToken ct) =>
        access.AllowedCompaniesAsync(user.UserId() ?? Guid.Empty, user.IsInRole(nameof(UserRole.SuperAdmin)), ct);
}
