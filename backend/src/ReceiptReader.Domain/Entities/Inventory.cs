using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Domain.Entities;

/// <summary>A group of products (เตียงนอน, ผ้าปู 6 ฟุต …). Its prefix is where suggested product codes start.</summary>
public class ProductCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    /// <summary>Unique per company.</summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// "BED6" → suggested codes of its products start BED6 (BED6123111). Unique per company. Changing it later
    /// leaves the codes of products already in the category as they are.
    /// </summary>
    public string CodePrefix { get; set; } = "";
    /// <summary>Switched off: kept for the products already in it, but new products can't be put in it.</summary>
    public bool Active { get; set; } = true;
    /// <summary>Position in the category list (0 first), set by dragging; new categories go last.</summary>
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Something the company buys and sells. <see cref="Code"/> is what the bookkeeping refers to and what the printed
/// Code 128 label encodes, so scanning a label finds the product directly.
/// </summary>
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid CategoryId { get; set; }
    public ProductCategory? Category { get; set; }
    /// <summary>
    /// Set by the user, usually from a suggestion built on the company's <see cref="ProductCodeScheme"/>
    /// (BED6 + Lotus 123 + ขาว 111 → BED6123111). Unique per company; bookkeeping and printed labels refer to it.
    /// A–Z, 0–9 and "-" only, so every barcode scanner can type it.
    /// </summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary><see cref="Name"/> lower-cased with whitespace collapsed; unique per company, so no two products share a name.</summary>
    public string NormalizedName { get; set; } = "";
    /// <summary>หน่วยนับ: ชิ้น, ชุด, หลัง …</summary>
    public string Unit { get; set; } = "ชิ้น";
    /// <summary>Usual selling price, printed on labels when asked. The cost comes from purchases, not from here.</summary>
    public decimal? SalePrice { get; set; }
    /// <summary>แจ้งเตือนเมื่อเหลือ (reorder point): the product counts as running low once the quantity on hand is at or below this.</summary>
    public decimal? ReorderPoint { get; set; }
    /// <summary>
    /// True for goods kept in a warehouse (quantity and value are tracked); false for services and fees such as
    /// delivery or installation, which are sold or bought without anything on hand.
    /// </summary>
    public bool TrackStock { get; set; } = true;
    /// <summary>False once the product is no longer sold: kept with its history, left out of new documents.</summary>
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A place stock is kept (โกดัง, หน้าร้าน, สาขา). Every company has exactly one default warehouse.</summary>
public class Warehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Where documents put stock unless another warehouse is chosen.</summary>
    public bool IsDefault { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One change to the quantity of a product in a warehouse. The quantity on hand and its value are the running
/// result of these rows (see StockLedger); nothing else stores a balance.
/// </summary>
public class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateOnly Date { get; set; }
    public StockMovementType Type { get; set; }
    /// <summary>Positive comes in, negative goes out.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Cost per unit of stock coming in (baht, before VAT); null for stock going out, which leaves at the average cost.</summary>
    public decimal? UnitCost { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// How a company names and codes its products, worked out with the user from what the business sells
/// (ร้านเฟอร์นิเจอร์: เตียง โต๊ะ เก้าอี้ …). The hints are shown next to the name and code fields; the segments
/// are the parts a suggested product code is built from, after the category prefix. One per company.
/// </summary>
public class ProductCodeScheme
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    /// <summary>ประเภทธุรกิจ: ร้านเฟอร์นิเจอร์, ร้านวัสดุก่อสร้าง …</summary>
    public string BusinessType { get; set; } = "";
    /// <summary>What the business sells, as the user put it: เตียง, ที่นอน, ผ้าปู …</summary>
    public List<string> ProductTypes { get; set; } = [];
    /// <summary>Anything else the user said about how products differ (แยกตามแบรนด์ ขนาด สี).</summary>
    public string? Notes { get; set; }
    /// <summary>How category names are written: "ประเภทสินค้า + ขนาด เช่น เตียงนอน 6 ฟุต".</summary>
    public string? CategoryNameHint { get; set; }
    /// <summary>How product names are written: "แบรนด์ รุ่น สี เช่น Lotus Burgi ขาว".</summary>
    public string? ProductNameHint { get; set; }
    /// <summary>The code structure in words: "รหัสหมวด + แบรนด์ 3 หลัก + สี 3 หลัก → BED6123111".</summary>
    public string? CodeHint { get; set; }
    /// <summary>The parts after the category prefix, in order.</summary>
    public List<CodeSegment> Segments { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One part of a product code: แบรนด์ as 3 digits, สี as 3 digits …</summary>
public class CodeSegment
{
    /// <summary>Stable id the learned terms refer to ("brand"); kept when the label is renamed.</summary>
    public string Key { get; set; } = "";
    /// <summary>What the user sees: แบรนด์, รุ่น, สี, ขนาด.</summary>
    public string Label { get; set; } = "";
    /// <summary>Characters this part takes in the code.</summary>
    public int Length { get; set; }
    /// <summary>True: 0–9 only (Lotus → 123); false: A–Z and 0–9 (Burgi → BG).</summary>
    public bool Digits { get; set; } = true;
}

/// <summary>
/// A learned code for one value of a segment: แบรนด์ Lotus = 123. Saved when a product is saved from a suggestion,
/// and given back to the AI so the next Lotus product gets 123 too. A term and a code are each unique per segment.
/// </summary>
public class ProductCodeTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    /// <summary><see cref="CodeSegment.Key"/>.</summary>
    public string Segment { get; set; } = "";
    /// <summary>As it appears in product names: Lotus, ขาว.</summary>
    public string Term { get; set; } = "";
    /// <summary><see cref="Term"/> lower-cased with whitespace collapsed.</summary>
    public string NormalizedTerm { get; set; } = "";
    public string Code { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
