using ReceiptReader.Api.Auth;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Learning;

namespace ReceiptReader.Api.Endpoints;

public static class LearningEndpoints
{
    public static IEndpointRouteBuilder MapLearningEndpoints(this IEndpointRouteBuilder app)
    {
        // Training data spans every company, so the whole area is for super admins.
        var g = app.MapGroup("/api/ai").WithTags("AI learning").RequireAuthorization(AuthSetup.SuperAdminPolicy);

        g.MapGet("/accuracy", (Guid? companyId, LearningService svc, CancellationToken ct) => svc.AccuracyAsync(companyId, ct));

        g.MapGet("/issuers", (LearningService svc, CancellationToken ct) => svc.IssuersAsync(ct));

        g.MapGet("/examples/{id:guid}", async (Guid id, LearningService svc, CancellationToken ct) =>
            Results.Content(await svc.ExampleJsonAsync(id, ct), "application/json; charset=utf-8"));

        g.MapPost("/examples/{id:guid}/pin", async (Guid id, LearningService svc, CancellationToken ct) =>
        {
            await svc.SetPinnedAsync(id, true, ct);
            return Results.NoContent();
        });

        g.MapPost("/examples/{id:guid}/unpin", async (Guid id, LearningService svc, CancellationToken ct) =>
        {
            await svc.SetPinnedAsync(id, false, ct);
            return Results.NoContent();
        });

        g.MapDelete("/examples/{id:guid}", async (Guid id, LearningService svc, CancellationToken ct) =>
        {
            await svc.DeleteExampleAsync(id, ct);
            return Results.NoContent();
        });

        g.MapPost("/examples/from-receipt/{receiptId:guid}", async (Guid receiptId, LearningService svc, CancellationToken ct) =>
        {
            await svc.UseAsExampleAsync(receiptId, ct);
            return Results.NoContent();
        });

        g.MapPost("/examples/from-receipt/{receiptId:guid}/unpin", async (Guid receiptId, LearningService svc, CancellationToken ct) =>
        {
            await svc.UnpinReceiptAsync(receiptId, ct);
            return Results.NoContent();
        });

        g.MapGet("/dataset.zip", async (Guid? companyId, LearningService svc, IFileStorage storage, CancellationToken ct) =>
        {
            // ZipArchive writes synchronously, which Kestrel forbids on the response body: build it in a temp file.
            var temp = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                81920, FileOptions.DeleteOnClose);
            await svc.WriteDatasetAsync(companyId, temp, storage, ct);
            temp.Position = 0;
            return Results.File(temp, "application/zip", $"receipt-dataset-{DateTime.Now:yyyyMMdd}.zip");
        });

        return app;
    }
}
