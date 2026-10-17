using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ReceiptReader.Api.Auth;
using ReceiptReader.Application.Inventory;

namespace ReceiptReader.Api.Endpoints;

/// <summary>Product categories, products, the code pattern and warehouses. Every role reads them; admins and super admins change them.</summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/companies").WithTags("Inventory")
            .RequireCompanyAccess(CompanyAccessFilter.RouteId.Company);

        // ---- categories ----
        g.MapGet("/{id:guid}/product-categories", (Guid id, InventoryService svc, CancellationToken ct) =>
            svc.ListCategoriesAsync(id, ct));
        g.MapPost("/{id:guid}/product-categories",
            (Guid id, [FromBody] ProductCategoryInput input, InventoryService svc, CancellationToken ct) =>
                svc.CreateCategoryAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPut("/{id:guid}/product-categories/order",
            async (Guid id, [FromBody] CategoryOrderInput input, InventoryService svc, CancellationToken ct) =>
            {
                await svc.ReorderCategoriesAsync(id, input.CategoryIds ?? [], ct);
                return Results.NoContent();
            }).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPut("/{id:guid}/product-categories/{categoryId:guid}",
            (Guid id, Guid categoryId, [FromBody] ProductCategoryInput input, InventoryService svc, CancellationToken ct) =>
                svc.UpdateCategoryAsync(id, categoryId, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapDelete("/{id:guid}/product-categories/{categoryId:guid}",
            async (Guid id, Guid categoryId, InventoryService svc, CancellationToken ct) =>
            {
                await svc.DeleteCategoryAsync(id, categoryId, ct);
                return Results.NoContent();
            }).RequireAuthorization(AuthSetup.ManagerPolicy);

        // ---- products ----
        g.MapGet("/{id:guid}/products", (Guid id, InventoryService svc, CancellationToken ct) =>
            svc.ListProductsAsync(id, ct));
        // A scanned label or a typed code; also understands a scan typed while the keyboard was in Thai.
        g.MapGet("/{id:guid}/products/lookup", async (Guid id, string? code, InventoryService svc, CancellationToken ct) =>
            await svc.LookupAsync(id, code, ct) is { } p
                ? Results.Ok(p)
                : Results.Problem(title: "Not found", detail: $"ไม่พบสินค้ารหัส {code?.Trim()}", statusCode: StatusCodes.Status404NotFound));
        // With its stock per warehouse and ยอดยกมา.
        g.MapGet("/{id:guid}/products/{productId:guid}", (Guid id, Guid productId, InventoryService svc, CancellationToken ct) =>
            svc.GetProductAsync(id, productId, ct));
        g.MapPost("/{id:guid}/products",
            (Guid id, [FromBody] ProductInput input, ClaimsPrincipal user, InventoryService svc, CancellationToken ct) =>
                svc.CreateProductAsync(id, input, user.Identity?.Name, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPut("/{id:guid}/products/{productId:guid}",
            (Guid id, Guid productId, [FromBody] ProductInput input, ClaimsPrincipal user, InventoryService svc, CancellationToken ct) =>
                svc.UpdateProductAsync(id, productId, input, user.Identity?.Name, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapDelete("/{id:guid}/products/{productId:guid}", async (Guid id, Guid productId, InventoryService svc, CancellationToken ct) =>
        {
            await svc.DeleteProductAsync(id, productId, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthSetup.ManagerPolicy);

        // ---- code pattern (questionnaire) and AI suggestions ----
        // 204 when the questionnaire hasn't been answered: the AI suggestions are off.
        g.MapGet("/{id:guid}/code-scheme", async (Guid id, CodeSchemeService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } s ? Results.Ok(s) : Results.NoContent());
        g.MapPost("/{id:guid}/code-scheme/propose",
            (Guid id, [FromBody] SchemeQuestion input, CodeSchemeService svc, CancellationToken ct) =>
                svc.ProposeAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPost("/{id:guid}/code-scheme/suggest-notes",
            (Guid id, [FromBody] SchemeQuestion input, CodeSchemeService svc, CancellationToken ct) =>
                svc.SuggestNotesAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPut("/{id:guid}/code-scheme",
            (Guid id, [FromBody] CodeSchemeInput input, CodeSchemeService svc, CancellationToken ct) =>
                svc.SaveAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapDelete("/{id:guid}/code-scheme", async (Guid id, CodeSchemeService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapDelete("/{id:guid}/code-scheme/terms/{termId:guid}", async (Guid id, Guid termId, CodeSchemeService svc, CancellationToken ct) =>
        {
            await svc.DeleteTermAsync(id, termId, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPost("/{id:guid}/code-scheme/suggest-category",
            (Guid id, [FromBody] CategoryCodeRequest input, CodeSchemeService svc, CancellationToken ct) =>
                svc.SuggestCategoryAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPost("/{id:guid}/code-scheme/suggest-product",
            (Guid id, [FromBody] ProductCodeRequest input, CodeSchemeService svc, CancellationToken ct) =>
                svc.SuggestProductAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);

        // ---- warehouses ----
        g.MapGet("/{id:guid}/warehouses", (Guid id, InventoryService svc, CancellationToken ct) =>
            svc.ListWarehousesAsync(id, ct));
        g.MapPost("/{id:guid}/warehouses", (Guid id, [FromBody] WarehouseInput input, InventoryService svc, CancellationToken ct) =>
            svc.CreateWarehouseAsync(id, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapPut("/{id:guid}/warehouses/{warehouseId:guid}",
            (Guid id, Guid warehouseId, [FromBody] WarehouseInput input, InventoryService svc, CancellationToken ct) =>
                svc.UpdateWarehouseAsync(id, warehouseId, input, ct)).RequireAuthorization(AuthSetup.ManagerPolicy);
        g.MapDelete("/{id:guid}/warehouses/{warehouseId:guid}", async (Guid id, Guid warehouseId, InventoryService svc, CancellationToken ct) =>
        {
            await svc.DeleteWarehouseAsync(id, warehouseId, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthSetup.ManagerPolicy);

        return app;
    }
}
