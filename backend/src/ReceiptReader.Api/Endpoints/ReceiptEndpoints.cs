using Microsoft.AspNetCore.Mvc;
using ReceiptReader.Api.Auth;
using ReceiptReader.Application.Receipts;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Api.Endpoints;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/receipts").WithTags("Receipts")
            .RequireCompanyAccess(CompanyAccessFilter.RouteId.Receipt);

        g.MapPost("/", async (Guid companyId, Direction direction, IFormFileCollection files, ReceiptService svc, CancellationToken ct) =>
            {
                if (files.Count == 0) return Results.BadRequest(new { error = "แนบไฟล์อย่างน้อย 1 ไฟล์ (field: files)" });
                var results = new List<object>();
                foreach (var f in files)
                {
                    try
                    {
                        await using var s = f.OpenReadStream();
                        var r = await svc.UploadAsync(s, f.FileName, f.ContentType, companyId, direction, ct);
                        results.Add(new { r.Id, r.FileName, r.Duplicate, Error = (string?)null });
                    }
                    catch (Application.Abstractions.BadRequestException ex)
                    {
                        results.Add(new { Id = (Guid?)null, FileName = f.FileName, Duplicate = false, Error = ex.Message });
                    }
                }
                return Results.Ok(results);
            })
            .DisableAntiforgery()
            .WithSummary("Upload documents for a company and side (?companyId=&direction=Purchase|Sale; multipart field: files)");

        g.MapPost("/manual/draft", ([FromBody] ManualReceiptRequest req, ReceiptService svc, CancellationToken ct) =>
                svc.DraftManualAsync(req, ct))
            .WithSummary("The blank form for a document typed in by hand (nothing is stored)");

        g.MapPost("/manual/validate", ([FromBody] ManualReceiptRequest req, ReceiptService svc, CancellationToken ct) =>
                svc.PreviewManualAsync(req, ct))
            .WithSummary("Validate an unsaved manual document without storing it");

        g.MapPost("/manual", ([FromBody] ManualReceiptRequest req, ReceiptService svc, CancellationToken ct) =>
                svc.CreateManualAsync(req, ct))
            .WithSummary("Store a document typed in by hand (no file; body: companyId, direction, data)");

        // ?status= may repeat: status=NeedsReview&status=Validated lists both.
        g.MapGet("/", (Guid companyId, Direction? direction, ReceiptStatus[]? status, string? period, string? search,
                int? page, int? pageSize, ReceiptService svc, CancellationToken ct) =>
            svc.ListAsync(new ReceiptListQuery(companyId, direction, status, period, search, page ?? 1, pageSize ?? 50), ct));

        g.MapGet("/stats", (Guid companyId, Direction? direction, string? period, ReceiptService svc, CancellationToken ct) =>
            svc.CountByStatusAsync(companyId, direction, period, ct));

        g.MapGet("/{id:guid}", (Guid id, ReceiptService svc, CancellationToken ct) => svc.GetAsync(id, ct));

        g.MapGet("/{id:guid}/file", async (Guid id, ReceiptService svc, CancellationToken ct) =>
        {
            var (content, contentType, _) = await svc.OpenFileAsync(id, ct);
            return Results.Stream(content, contentType, enableRangeProcessing: true);
        });

        g.MapPut("/{id:guid}", (Guid id, [FromBody] ReceiptUpdateDto dto, ReceiptService svc, CancellationToken ct) =>
            svc.UpdateAsync(id, dto, ct));

        g.MapPost("/{id:guid}/validate", (Guid id, [FromBody] ReceiptUpdateDto dto, ReceiptService svc, CancellationToken ct) =>
                svc.PreviewAsync(id, dto, ct))
            .WithSummary("Validate unsaved edits without storing them");

        // Approving and payments are bookkeeping decisions: approvers and up. Users record documents only.
        g.MapPost("/{id:guid}/approve", (Guid id, [FromBody] ApproveRequest? req, ReceiptService svc, CancellationToken ct) =>
            svc.ApproveAsync(id, req ?? new ApproveRequest(), ct)).RequireAuthorization(AuthSetup.ApproverPolicy);

        g.MapPost("/{id:guid}/unapprove", (Guid id, ReceiptService svc, CancellationToken ct) =>
            svc.UnapproveAsync(id, ct)).RequireAuthorization(AuthSetup.ApproverPolicy);

        g.MapPost("/{id:guid}/reprocess", async (Guid id, ReceiptService svc, CancellationToken ct) =>
        {
            await svc.ReprocessAsync(id, ct);
            return Results.Accepted();
        });

        g.MapDelete("/{id:guid}", async (Guid id, ReceiptService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/payments", (Guid id, [FromBody] PaymentInput input, ReceiptService svc, CancellationToken ct) =>
            svc.AddPaymentAsync(id, input, ct)).RequireAuthorization(AuthSetup.ApproverPolicy);

        g.MapDelete("/{id:guid}/payments/{paymentId:guid}", (Guid id, Guid paymentId, ReceiptService svc, CancellationToken ct) =>
            svc.DeletePaymentAsync(id, paymentId, ct)).RequireAuthorization(AuthSetup.ApproverPolicy);

        return app;
    }
}
