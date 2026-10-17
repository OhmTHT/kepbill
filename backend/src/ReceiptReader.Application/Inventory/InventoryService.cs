using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Accounting;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Inventory;

public sealed record ProductCategoryDto(Guid Id, string Name, string CodePrefix, bool Active, int ProductCount);

/// <param name="Active">Null keeps it as it is (a new category starts active).</param>
public sealed record ProductCategoryInput(string Name, string? CodePrefix, bool? Active = null);

/// <param name="CategoryIds">Every category of the company, first to last.</param>
public sealed record CategoryOrderInput(IReadOnlyList<Guid> CategoryIds);

/// <param name="OnHand">Across every warehouse; 0 for services.</param>
/// <param name="AverageCost">Moving average cost per unit (baht).</param>
/// <param name="StockValue">OnHand at the average cost (baht).</param>
public sealed record ProductDto(
    Guid Id, Guid CategoryId, string CategoryName, string Code, string Name, string Unit,
    decimal? SalePrice, decimal? ReorderPoint, bool TrackStock, bool Active,
    decimal OnHand, decimal AverageCost, decimal StockValue, DateTimeOffset UpdatedAt);

/// <param name="OpeningQuantity">ยอดยกมา in this warehouse, or null when there is none.</param>
public sealed record WarehouseStockDto(
    Guid WarehouseId, string WarehouseName, bool Active, decimal OnHand, decimal? OpeningQuantity, decimal? OpeningUnitCost);

/// <param name="OpeningDate">The date of the ยอดยกมา, or null when there is none.</param>
public sealed record ProductDetailDto(ProductDto Product, DateOnly? OpeningDate, IReadOnlyList<WarehouseStockDto> Warehouses);

/// <summary>ยอดยกมา of a product in one warehouse.</summary>
public sealed record OpeningStockInput(Guid WarehouseId, decimal Quantity, decimal? UnitCost);

/// <param name="Openings">
/// The whole ยอดยกมา of the product, one row per warehouse that has some: warehouses left out have none.
/// Null leaves it as it is. Ignored for services.
/// </param>
/// <param name="Code">Required; the user's own or an AI suggestion.</param>
/// <param name="CodeParts">
/// The parts of an AI suggestion the code was taken from (แบรนด์ Lotus = 123 …): codes the company doesn't know
/// yet are remembered for the next suggestion. Left out when the code was typed by hand.
/// </param>
public sealed record ProductInput(
    Guid CategoryId, string Name, string? Unit, decimal? SalePrice, decimal? ReorderPoint,
    bool TrackStock = true, bool Active = true, DateOnly? OpeningDate = null, IReadOnlyList<OpeningStockInput>? Openings = null,
    string? Code = null, IReadOnlyList<CodePart>? CodeParts = null);

public sealed record WarehouseDto(Guid Id, string Name, bool IsDefault, bool Active);

public sealed record WarehouseInput(string Name, bool IsDefault, bool Active = true);

/// <summary>Product categories, products, warehouses and the stock of a company.</summary>
public sealed class InventoryService(IAppDbContext db, IClock clock)
{
    public const string DefaultWarehouseName = "คลังหลัก";

    public static Warehouse DefaultWarehouse(Guid companyId) =>
        new() { CompanyId = companyId, Name = DefaultWarehouseName, IsDefault = true };

    /// <summary>Give every company without a warehouse its default one. Idempotent.</summary>
    public async Task EnsureWarehousesAsync(CancellationToken ct)
    {
        var missing = await db.Companies
            .Where(c => !db.Warehouses.Any(w => w.CompanyId == c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var id in missing) db.Warehouses.Add(DefaultWarehouse(id));
        await db.SaveChangesAsync(ct);
    }

    // ---------------- categories ----------------

    public async Task<IReadOnlyList<ProductCategoryDto>> ListCategoriesAsync(Guid companyId, CancellationToken ct) =>
        await db.ProductCategories.AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductCategoryDto(c.Id, c.Name, c.CodePrefix, c.Active, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<ProductCategoryDto> CreateCategoryAsync(Guid companyId, ProductCategoryInput input, CancellationToken ct)
    {
        await RequireCompanyAsync(companyId, ct);
        var last = await db.ProductCategories.Where(x => x.CompanyId == companyId).MaxAsync(x => (int?)x.SortOrder, ct);
        var c = new ProductCategory { CompanyId = companyId, CreatedAt = clock.Now, SortOrder = (last ?? -1) + 1 };
        await ApplyAsync(input, c, ct);
        db.ProductCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return new ProductCategoryDto(c.Id, c.Name, c.CodePrefix, c.Active, 0);
    }

    public async Task<ProductCategoryDto> UpdateCategoryAsync(Guid companyId, Guid categoryId, ProductCategoryInput input, CancellationToken ct)
    {
        var c = await FindCategoryAsync(companyId, categoryId, ct);
        var products = await db.Products.CountAsync(p => p.CategoryId == c.Id, ct);
        await ApplyAsync(input, c, ct);
        await db.SaveChangesAsync(ct);
        return new ProductCategoryDto(c.Id, c.Name, c.CodePrefix, c.Active, products);
    }

    /// <summary>Puts the categories in the given order: every category of the company, each once.</summary>
    public async Task ReorderCategoriesAsync(Guid companyId, IReadOnlyList<Guid> categoryIds, CancellationToken ct)
    {
        var categories = await db.ProductCategories.Where(c => c.CompanyId == companyId).ToListAsync(ct);
        if (categoryIds.Count != categories.Count || !categories.All(c => categoryIds.Contains(c.Id)))
            throw new ConflictException("รายการหมวดสินค้าเปลี่ยนไปแล้ว — โหลดหน้าใหม่แล้วลองอีกครั้ง");
        var position = categoryIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        foreach (var c in categories) c.SortOrder = position[c.Id];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Only an empty category can be removed; one with products is switched off instead.</summary>
    public async Task DeleteCategoryAsync(Guid companyId, Guid categoryId, CancellationToken ct)
    {
        var c = await FindCategoryAsync(companyId, categoryId, ct);
        var products = await db.Products.CountAsync(p => p.CategoryId == c.Id, ct);
        if (products > 0) throw new ConflictException($"หมวด {c.Name} มีสินค้า {products} รายการ ลบไม่ได้ — ปิดใช้งานแทน");
        db.ProductCategories.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- products ----------------

    public async Task<IReadOnlyList<ProductDto>> ListProductsAsync(Guid companyId, CancellationToken ct) =>
        await ProductsAsync(companyId, null, ct);

    public async Task<ProductDetailDto> GetProductAsync(Guid companyId, Guid productId, CancellationToken ct)
    {
        var product = (await ProductsAsync(companyId, productId, ct)).FirstOrDefault()
                      ?? throw new NotFoundException($"ไม่พบสินค้า {productId}");
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .Select(m => new { m.WarehouseId, m.Type, m.Quantity, m.UnitCost, m.Date })
            .ToListAsync(ct);
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => w.CompanyId == companyId)
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .ToListAsync(ct);
        var openings = movements.Where(m => m.Type == StockMovementType.Opening).ToList();
        var rows = warehouses
            // Switched-off warehouses only matter while they still hold some of this product.
            .Where(w => w.Active || movements.Any(m => m.WarehouseId == w.Id))
            .Select(w =>
            {
                var opening = openings.FirstOrDefault(m => m.WarehouseId == w.Id);
                return new WarehouseStockDto(w.Id, w.Name, w.Active,
                    movements.Where(m => m.WarehouseId == w.Id).Sum(m => m.Quantity),
                    opening?.Quantity, opening?.UnitCost);
            })
            .ToList();
        return new ProductDetailDto(product, openings.Select(m => (DateOnly?)m.Date).FirstOrDefault(), rows);
    }

    /// <summary>The product a scanned or typed code stands for, or null.</summary>
    public async Task<ProductDto?> LookupAsync(Guid companyId, string? code, CancellationToken ct)
    {
        var candidates = ProductCodes.ScanCandidates(code);
        if (candidates.Count == 0) return null;
        var found = await db.Products.AsNoTracking()
            .Where(p => p.CompanyId == companyId && candidates.Contains(p.Code))
            .Select(p => new { p.Id, p.Code })
            .ToListAsync(ct);
        // The text as typed wins over its English-keyboard reading.
        var id = candidates.Select(c => found.FirstOrDefault(p => p.Code == c)?.Id).FirstOrDefault(i => i is not null);
        return id is { } productId ? (await ProductsAsync(companyId, productId, ct)).FirstOrDefault() : null;
    }

    public async Task<ProductDetailDto> CreateProductAsync(Guid companyId, ProductInput input, string? user, CancellationToken ct)
    {
        await RequireCompanyAsync(companyId, ct);
        var p = new Product { CompanyId = companyId, CreatedAt = clock.Now };
        await ApplyAsync(input, p, ct);
        db.Products.Add(p);
        await ApplyOpeningAsync(p, input, user, ct);
        await LearnCodeAsync(p, input.CodeParts, ct);
        await db.SaveChangesAsync(ct);
        return await GetProductAsync(companyId, p.Id, ct);
    }

    public async Task<ProductDetailDto> UpdateProductAsync(Guid companyId, Guid productId, ProductInput input, string? user, CancellationToken ct)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == productId && x.CompanyId == companyId, ct)
                ?? throw new NotFoundException($"ไม่พบสินค้า {productId}");
        await ApplyAsync(input, p, ct);
        await ApplyOpeningAsync(p, input, user, ct);
        await LearnCodeAsync(p, input.CodeParts, ct);
        await db.SaveChangesAsync(ct);
        return await GetProductAsync(companyId, p.Id, ct);
    }

    /// <summary>Only a product without stock history can be removed; one that has some is switched off (เลิกขาย) instead.</summary>
    public async Task DeleteProductAsync(Guid companyId, Guid productId, CancellationToken ct)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == productId && x.CompanyId == companyId, ct)
                ?? throw new NotFoundException($"ไม่พบสินค้า {productId}");
        if (await db.StockMovements.AnyAsync(m => m.ProductId == p.Id && m.Type != StockMovementType.Opening, ct))
            throw new ConflictException($"{p.Code} มีประวัติเข้า-ออกสต็อกแล้ว ลบไม่ได้ — ตั้งเป็นเลิกขายแทน");
        db.StockMovements.RemoveRange(await db.StockMovements.Where(m => m.ProductId == p.Id).ToListAsync(ct));
        db.Products.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Products with their stock: quantity and moving-average value, worked out from the movements.</summary>
    private async Task<List<ProductDto>> ProductsAsync(Guid companyId, Guid? only, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.CompanyId == companyId && (only == null || p.Id == only))
            .OrderBy(p => p.Code)
            .Select(p => new { p, CategoryName = p.Category!.Name })
            .ToListAsync(ct);
        var lines = (await db.StockMovements.AsNoTracking()
                .Where(m => m.CompanyId == companyId && (only == null || m.ProductId == only))
                .Select(m => new { m.ProductId, m.Date, m.CreatedAt, m.Type, m.Quantity, m.UnitCost })
                .ToListAsync(ct))
            .ToLookup(m => m.ProductId, m => new StockLine(m.Date, m.CreatedAt, m.Type, m.Quantity, m.UnitCost));

        return products.Select(x =>
        {
            var p = x.p;
            var stock = p.TrackStock ? StockLedger.Run(lines[p.Id]) : StockPosition.Empty;
            return new ProductDto(p.Id, p.CategoryId, x.CategoryName, p.Code, p.Name, p.Unit, p.SalePrice, p.ReorderPoint,
                p.TrackStock, p.Active, stock.Quantity, Math.Round(stock.AverageCost, 4), Math.Round(stock.Value, 2), p.UpdatedAt);
        }).ToList();
    }

    // ---------------- warehouses ----------------

    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid companyId, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking()
            .Where(w => w.CompanyId == companyId)
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .Select(w => new WarehouseDto(w.Id, w.Name, w.IsDefault, w.Active))
            .ToListAsync(ct);

    public async Task<WarehouseDto> CreateWarehouseAsync(Guid companyId, WarehouseInput input, CancellationToken ct)
    {
        await RequireCompanyAsync(companyId, ct);
        var w = new Warehouse { CompanyId = companyId, CreatedAt = clock.Now };
        // The first warehouse of a company is its default whatever was asked.
        var first = !await db.Warehouses.AnyAsync(x => x.CompanyId == companyId, ct);
        await ApplyAsync(first ? input with { IsDefault = true, Active = true } : input, w, ct);
        db.Warehouses.Add(w);
        await db.SaveChangesAsync(ct);
        return new WarehouseDto(w.Id, w.Name, w.IsDefault, w.Active);
    }

    public async Task<WarehouseDto> UpdateWarehouseAsync(Guid companyId, Guid warehouseId, WarehouseInput input, CancellationToken ct)
    {
        var w = await FindWarehouseAsync(companyId, warehouseId, ct);
        if (w.IsDefault && !input.IsDefault) throw new BadRequestException("ตั้งคลังอื่นเป็นคลังหลักแทน แล้วคลังนี้จะไม่เป็นคลังหลักเอง");
        await ApplyAsync(input, w, ct);
        await db.SaveChangesAsync(ct);
        return new WarehouseDto(w.Id, w.Name, w.IsDefault, w.Active);
    }

    public async Task DeleteWarehouseAsync(Guid companyId, Guid warehouseId, CancellationToken ct)
    {
        var w = await FindWarehouseAsync(companyId, warehouseId, ct);
        if (w.IsDefault) throw new ConflictException($"{w.Name} เป็นคลังหลัก ลบไม่ได้ — ตั้งคลังอื่นเป็นคลังหลักก่อน");
        if (await db.StockMovements.AnyAsync(m => m.WarehouseId == w.Id, ct))
            throw new ConflictException($"{w.Name} มีประวัติสินค้าเข้า-ออกแล้ว ลบไม่ได้ — ปิดใช้งานแทน");
        db.Warehouses.Remove(w);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------

    private async Task RequireCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) throw new NotFoundException($"ไม่พบบริษัท {companyId}");
    }

    private async Task<ProductCategory> FindCategoryAsync(Guid companyId, Guid categoryId, CancellationToken ct) =>
        await db.ProductCategories.FirstOrDefaultAsync(x => x.Id == categoryId && x.CompanyId == companyId, ct)
        ?? throw new NotFoundException($"ไม่พบหมวดสินค้า {categoryId}");

    private async Task<Warehouse> FindWarehouseAsync(Guid companyId, Guid warehouseId, CancellationToken ct) =>
        await db.Warehouses.FirstOrDefaultAsync(x => x.Id == warehouseId && x.CompanyId == companyId, ct)
        ?? throw new NotFoundException($"ไม่พบคลังสินค้า {warehouseId}");

    private async Task ApplyAsync(ProductCategoryInput input, ProductCategory c, CancellationToken ct)
    {
        var name = RequireText(input.Name, 200, "กรอกชื่อหมวดสินค้า");
        if (await db.ProductCategories.AnyAsync(x => x.CompanyId == c.CompanyId && x.Name == name && x.Id != c.Id, ct))
            throw new ConflictException($"มีหมวดชื่อ {name} อยู่แล้ว");
        var prefix = ProductCodes.NormalizePrefix(input.CodePrefix);
        // Codes of products already in the category stay as they are: labels have been printed with them.
        if (prefix != c.CodePrefix &&
            await db.ProductCategories.AnyAsync(x => x.CompanyId == c.CompanyId && x.CodePrefix == prefix && x.Id != c.Id, ct))
            throw new ConflictException($"มีหมวดที่ใช้รหัส {prefix} อยู่แล้ว");
        c.Name = name;
        c.CodePrefix = prefix;
        if (input.Active is { } active) c.Active = active;
    }

    private async Task ApplyAsync(ProductInput input, Product p, CancellationToken ct)
    {
        var category = await db.ProductCategories.AsNoTracking()
                           .FirstOrDefaultAsync(c => c.Id == input.CategoryId && c.CompanyId == p.CompanyId, ct)
                       ?? throw new BadRequestException("เลือกหมวดสินค้า");
        // Products already in a switched-off category may stay there; nothing new goes in.
        if (!category.Active && category.Id != p.CategoryId)
            throw new BadRequestException($"หมวด {category.Name} ปิดใช้งานแล้ว เลือกหมวดอื่น");
        var name = RequireText(ProductNames.Clean(input.Name), 300, "กรอกชื่อสินค้า");
        var key = ProductNames.Key(name);
        var same = await db.Products.AsNoTracking()
            .Where(x => x.CompanyId == p.CompanyId && x.NormalizedName == key && x.Id != p.Id)
            .Select(x => new { x.Code, x.Active })
            .FirstOrDefaultAsync(ct);
        if (same is not null)
            throw new ConflictException($"มีสินค้าชื่อ {name} อยู่แล้ว ({same.Code}{(same.Active ? "" : " · เลิกขาย")}) — ตั้งชื่อให้ต่างกัน");
        if (input.SalePrice < 0) throw new BadRequestException("ราคาขายติดลบไม่ได้");
        if (input.ReorderPoint < 0) throw new BadRequestException("จำนวนแจ้งเตือนติดลบไม่ได้");

        var code = ProductCodes.NormalizeCode(input.Code);
        if (code != p.Code)
        {
            var taken = await db.Products.AsNoTracking()
                .Where(x => x.CompanyId == p.CompanyId && x.Code == code && x.Id != p.Id)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
            if (taken is not null) throw new ConflictException($"รหัส {code} ใช้กับสินค้า {taken} แล้ว — ตั้งรหัสอื่น");
        }

        p.Code = code;
        p.CategoryId = category.Id;
        p.Name = name;
        p.NormalizedName = key;
        p.Unit = string.IsNullOrWhiteSpace(input.Unit) ? "ชิ้น" : RequireText(input.Unit, 30, "");
        p.SalePrice = input.SalePrice;
        p.ReorderPoint = input.TrackStock ? input.ReorderPoint : null;
        p.TrackStock = input.TrackStock;
        p.Active = input.Active;
        p.UpdatedAt = clock.Now;
    }

    /// <summary>Remembers the codes of a suggestion the product was saved with that the company doesn't know yet.</summary>
    private async Task LearnCodeAsync(Product p, IReadOnlyList<CodePart>? parts, CancellationToken ct)
    {
        if (parts is not { Count: > 0 }) return;
        var known = await db.ProductCodeTerms.AsNoTracking().Where(t => t.CompanyId == p.CompanyId).ToListAsync(ct);
        foreach (var t in CodeSuggestions.NewTerms(p.Code, parts, known))
            db.ProductCodeTerms.Add(new ProductCodeTerm
            {
                CompanyId = p.CompanyId,
                Segment = t.Segment,
                Term = t.Term,
                NormalizedTerm = t.NormalizedTerm,
                Code = t.Code,
                CreatedAt = clock.Now,
            });
    }

    /// <summary>
    /// Replaces the ยอดยกมา of a product with what was asked: one Opening movement per warehouse, all on the same
    /// date. Nothing changes in a closed month.
    /// </summary>
    private async Task ApplyOpeningAsync(Product p, ProductInput input, string? user, CancellationToken ct)
    {
        var existing = await db.StockMovements
            .Where(m => m.ProductId == p.Id && m.Type == StockMovementType.Opening)
            .ToListAsync(ct);
        if (input.Openings is null && p.TrackStock) return;

        var wanted = p.TrackStock ? input.Openings!.Where(o => o.Quantity != 0).ToList() : [];
        if (wanted.GroupBy(o => o.WarehouseId).Any(g => g.Count() > 1)) throw new BadRequestException("กรอกยอดยกมาคลังละ 1 แถว");
        foreach (var o in wanted)
        {
            if (o.Quantity < 0) throw new BadRequestException("จำนวนยอดยกมาติดลบไม่ได้");
            if (o.UnitCost is null or < 0) throw new BadRequestException("กรอกต้นทุนต่อหน่วยของยอดยกมา (ใช้คิดมูลค่าสินค้าคงเหลือ)");
        }
        var date = input.OpeningDate ?? clock.TodayInThailand;
        if (wanted.Count > 0 && input.OpeningDate is null) throw new BadRequestException("ระบุวันที่ของยอดยกมา");

        var unchanged = existing.Count == wanted.Count && wanted.All(o => existing.Any(m =>
            m.WarehouseId == o.WarehouseId && m.Quantity == o.Quantity && m.UnitCost == o.UnitCost && m.Date == date));
        if (unchanged) return;

        var warehouseIds = wanted.Select(o => o.WarehouseId).ToList();
        var warehouses = await db.Warehouses.Where(w => w.CompanyId == p.CompanyId && warehouseIds.Contains(w.Id)).CountAsync(ct);
        if (warehouses != warehouseIds.Count) throw new BadRequestException("ไม่พบคลังสินค้าที่เลือก");

        // Both the months the old rows leave and the month the new ones land in must be open.
        var touched = existing.Select(m => Periods.Of(m.Date)).ToHashSet();
        if (wanted.Count > 0) touched.Add(Periods.Of(date));
        var closed = await db.ClosedPeriods
            .Where(c => c.CompanyId == p.CompanyId && touched.Contains(c.Period))
            .Select(c => c.Period).FirstOrDefaultAsync(ct);
        if (closed is not null) throw new BadRequestException($"งวด {closed} ปิดแล้ว แก้ยอดยกมาในเดือนนั้นไม่ได้");

        db.StockMovements.RemoveRange(existing);
        foreach (var o in wanted)
            db.StockMovements.Add(new StockMovement
            {
                CompanyId = p.CompanyId,
                ProductId = p.Id,
                WarehouseId = o.WarehouseId,
                Date = date,
                Type = StockMovementType.Opening,
                Quantity = o.Quantity,
                UnitCost = o.UnitCost,
                Note = "ยอดยกมา",
                CreatedBy = user,
                CreatedAt = clock.Now,
            });
    }

    private async Task ApplyAsync(WarehouseInput input, Warehouse w, CancellationToken ct)
    {
        var name = RequireText(input.Name, 200, "กรอกชื่อคลังสินค้า");
        if (await db.Warehouses.AnyAsync(x => x.CompanyId == w.CompanyId && x.Name == name && x.Id != w.Id, ct))
            throw new ConflictException($"มีคลัง {name} อยู่แล้ว");
        if (input.IsDefault && !input.Active) throw new BadRequestException("คลังหลักต้องเปิดใช้งาน");

        if (input.IsDefault && !w.IsDefault)
            await db.Warehouses.Where(x => x.CompanyId == w.CompanyId && x.IsDefault && x.Id != w.Id)
                .ForEachAsync(x => x.IsDefault = false, ct);

        w.Name = name;
        w.IsDefault = input.IsDefault;
        w.Active = input.Active;
    }

    private static string RequireText(string? value, int max, string missing)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new BadRequestException(missing);
        return v.Length > max ? v[..max] : v;
    }
}
