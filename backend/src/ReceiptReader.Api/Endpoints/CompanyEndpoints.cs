using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ReceiptReader.Api.Auth;
using ReceiptReader.Application.Accounting;
using ReceiptReader.Application.Companies;
using ReceiptReader.Application.Users;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Api.Endpoints;

public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/companies").WithTags("Companies")
            .RequireCompanyAccess(CompanyAccessFilter.RouteId.Company);

        g.MapGet("/", async (ClaimsPrincipal user, CompanyAccess access, CompanyService svc, CancellationToken ct) =>
            await svc.ListAsync(ct, await access.AllowedCompaniesAsync(user, ct)));
        g.MapGet("/{id:guid}", (Guid id, CompanyService svc, CancellationToken ct) => svc.GetAsync(id, ct));
        // Only super admins add, edit or remove companies; admins manage the chart of accounts of their own.
        g.MapPost("/", ([FromBody] CompanyInput input, CompanyService svc, CancellationToken ct) => svc.CreateAsync(input, ct))
            .RequireAuthorization(AuthSetup.SuperAdminPolicy);
        g.MapPut("/{id:guid}", (Guid id, [FromBody] CompanyInput input, CompanyService svc, CancellationToken ct) =>
            svc.UpdateAsync(id, input, ct)).RequireAuthorization(AuthSetup.SuperAdminPolicy);
        g.MapDelete("/{id:guid}", async (Guid id, CompanyService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthSetup.SuperAdminPolicy);

        // ---- chart of accounts ----
        g.MapGet("/{id:guid}/accounts", (Guid id, CompanyService svc, CancellationToken ct) => svc.ListAccountsAsync(id, ct));
        g.MapPost("/{id:guid}/accounts", (Guid id, [FromBody] AccountInput input, CompanyService svc, CancellationToken ct) =>
            svc.CreateAccountAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPut("/{id:guid}/accounts/{accountId:guid}",
            (Guid id, Guid accountId, [FromBody] AccountInput input, CompanyService svc, CancellationToken ct) =>
                svc.UpdateAccountAsync(id, accountId, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapDelete("/{id:guid}/accounts/{accountId:guid}", async (Guid id, Guid accountId, CompanyService svc, CancellationToken ct) =>
        {
            await svc.DeleteAccountAsync(id, accountId, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthSetup.ManagerPolicy);

        // ---- periods ----
        // Readable by every role: the document list's period picker uses it.
        g.MapGet("/{id:guid}/periods", (Guid id, AccountingService svc, CancellationToken ct) => svc.PeriodsAsync(id, ct))
            .WithTags("Accounting");
        g.MapPost("/{id:guid}/periods/{period}/close",
            async (Guid id, string period, [FromBody] ClosePeriodRequest? req, ClaimsPrincipal user, AccountingService svc,
                CancellationToken ct) =>
            {
                await svc.CloseAsync(id, period, new ClosePeriodRequest(user.Identity?.Name ?? req?.ClosedBy), ct);
                return Results.NoContent();
            }).WithTags("Accounting").RequireAuthorization(AuthSetup.ApproverPolicy);
        g.MapPost("/{id:guid}/periods/{period}/reopen", async (Guid id, string period, AccountingService svc, CancellationToken ct) =>
        {
            await svc.ReopenAsync(id, period, ct);
            return Results.NoContent();
        }).WithTags("Accounting").RequireAuthorization(AuthSetup.ApproverPolicy);

        // ---- journal / trial balance / outstanding ----
        g.MapGet("/{id:guid}/journal", (Guid id, string period, AccountingService svc, CancellationToken ct) =>
            svc.JournalAsync(id, period, ct)).WithTags("Accounting").RequireAuthorization(AuthSetup.ApproverPolicy);
        g.MapGet("/{id:guid}/journal.csv", async (Guid id, string period, AccountingService svc, CancellationToken ct) =>
            Results.File(await svc.JournalCsvAsync(id, period, ct), "text/csv; charset=utf-8", $"journal-{period}.csv"))
            .WithTags("Accounting").RequireAuthorization(AuthSetup.ApproverPolicy);
        g.MapGet("/{id:guid}/trial-balance", (Guid id, string period, AccountingService svc, CancellationToken ct) =>
            svc.TrialBalanceAsync(id, period, ct)).WithTags("Accounting").RequireAuthorization(AuthSetup.ApproverPolicy);
        g.MapGet("/{id:guid}/outstanding", (Guid id, Direction direction, AccountingService svc, CancellationToken ct) =>
            svc.OutstandingAsync(id, direction, ct)).WithTags("Accounting").RequireAuthorization(AuthSetup.ApproverPolicy);

        return app;
    }
}
