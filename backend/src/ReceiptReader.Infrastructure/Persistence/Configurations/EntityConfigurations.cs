using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReceiptReader.Domain.Entities;

namespace ReceiptReader.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ToTable("Receipts");
        b.HasKey(x => x.Id);

        b.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        // Null for manual entries; PostgreSQL allows any number of NULLs in a unique index.
        b.Property(x => x.StoredPath).HasMaxLength(500);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.Sha256 }).IsUnique();

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.IssueDate);
        b.HasIndex(x => x.SellerTaxId);
        b.HasIndex(x => x.BuyerTaxId);
        b.HasIndex(x => new { x.CompanyId, x.Direction, x.Period });
        b.HasIndex(x => x.ReferenceReceiptId);

        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Period).HasMaxLength(7);
        b.Property(x => x.ReferenceDocumentNumber).HasMaxLength(100);
        b.Property(x => x.OwnTaxIdOnDocument).HasMaxLength(13);

        b.Property(x => x.ExtractorName).HasMaxLength(50);
        b.Property(x => x.ModelName).HasMaxLength(100);
        b.Property(x => x.RawExtraction).HasColumnType("jsonb");

        b.Property(x => x.DocumentNumber).HasMaxLength(100);
        b.Property(x => x.SellerName).HasMaxLength(300);
        b.Property(x => x.SellerTaxId).HasMaxLength(13);
        b.Property(x => x.SellerBranch).HasMaxLength(50);
        b.Property(x => x.BuyerName).HasMaxLength(300);
        b.Property(x => x.BuyerTaxId).HasMaxLength(13);
        b.Property(x => x.BuyerBranch).HasMaxLength(50);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.PaymentMethod).HasMaxLength(100);

        foreach (var p in new[] { nameof(Receipt.Subtotal), nameof(Receipt.Discount), nameof(Receipt.VatAmount), nameof(Receipt.Total), nameof(Receipt.WhtAmount) })
            b.Property(p).HasPrecision(18, 2);
        b.Property(x => x.WhtRate).HasPrecision(5, 2);

        // Validation issues live with the receipt as a jsonb array.
        b.Property(x => x.Issues)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<ValidationIssue>>(v, Json) ?? new List<ValidationIssue>(),
                new ValueComparer<List<ValidationIssue>>(
                    (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => v.ToList()));

        b.HasMany(x => x.LineItems).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReferenceReceiptId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.CounterpartyName);
        b.Ignore(x => x.CounterpartyTaxId);
        b.Ignore(x => x.IsAdjustmentNote);
        b.Ignore(x => x.IsManual);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.WhtAmount).HasPrecision(18, 2);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => x.Date);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("Companies");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.TaxId).HasMaxLength(13).IsRequired();
        b.Property(x => x.Branch).HasMaxLength(50);
        b.HasMany(x => x.Accounts).WithOne().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.NormalizedName).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
    }
}

internal sealed class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    public void Configure(EntityTypeBuilder<UserCompany> b)
    {
        b.ToTable("UserCompanies");
        b.HasKey(x => new { x.UserId, x.CompanyId });
        b.HasIndex(x => x.CompanyId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class ClosedPeriodConfiguration : IEntityTypeConfiguration<ClosedPeriod>
{
    public void Configure(EntityTypeBuilder<ClosedPeriod> b)
    {
        b.ToTable("ClosedPeriods");
        b.HasKey(x => x.Id);
        b.Property(x => x.Period).HasMaxLength(7).IsRequired();
        b.Property(x => x.ClosedBy).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.Period }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReceiptLineItemConfiguration : IEntityTypeConfiguration<ReceiptLineItem>
{
    public void Configure(EntityTypeBuilder<ReceiptLineItem> b)
    {
        b.ToTable("ReceiptLineItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Property(x => x.Discount).HasPrecision(18, 2);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.ReceiptId, x.LineNo });
    }
}

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("ProductCategories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.CodePrefix).HasMaxLength(6).IsRequired();
        b.Property(x => x.Active).HasDefaultValue(true);
        b.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.CodePrefix }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(300).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.NormalizedName }).IsUnique();
        b.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        b.Property(x => x.SalePrice).HasPrecision(18, 2);
        b.Property(x => x.ReorderPoint).HasPrecision(18, 4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasIndex(x => x.CategoryId);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        // A category with products can't be removed; CompanyService.DeleteAsync removes products first.
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("Warehouses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("StockMovements");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.Note).HasMaxLength(300);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.ProductId, x.Date });
        b.HasIndex(x => x.WarehouseId);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        // History keeps its product and warehouse: those are switched off, not removed, once they have some.
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> b)
    {
        b.ToTable("Vendors");
        b.HasKey(x => x.Id);
        b.Property(x => x.TaxId).HasMaxLength(13).IsRequired();
        b.HasIndex(x => x.TaxId).IsUnique();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Branch).HasMaxLength(50);
        b.HasMany(x => x.Examples).WithOne().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VendorExampleConfiguration : IEntityTypeConfiguration<VendorExample>
{
    public void Configure(EntityTypeBuilder<VendorExample> b)
    {
        b.ToTable("VendorExamples");
        b.HasKey(x => x.Id);
        b.Property(x => x.ApprovedJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.ReceiptId);
    }
}

internal sealed class ProductCodeSchemeConfiguration : IEntityTypeConfiguration<ProductCodeScheme>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ProductCodeScheme> b)
    {
        b.ToTable("ProductCodeSchemes");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.Property(x => x.BusinessType).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CategoryNameHint).HasMaxLength(300);
        b.Property(x => x.ProductNameHint).HasMaxLength(300);
        b.Property(x => x.CodeHint).HasMaxLength(500);
        // A short list read and written whole: kept as jsonb next to the rest of the scheme.
        b.Property(x => x.Segments)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<CodeSegment>>(v, Json) ?? new List<CodeSegment>(),
                new ValueComparer<List<CodeSegment>>(
                    (a, c) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(c, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<CodeSegment>>(JsonSerializer.Serialize(v, Json), Json)!));
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProductCodeTermConfiguration : IEntityTypeConfiguration<ProductCodeTerm>
{
    public void Configure(EntityTypeBuilder<ProductCodeTerm> b)
    {
        b.ToTable("ProductCodeTerms");
        b.HasKey(x => x.Id);
        b.Property(x => x.Segment).HasMaxLength(40).IsRequired();
        b.Property(x => x.Term).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedTerm).HasMaxLength(100).IsRequired();
        b.Property(x => x.Code).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Segment, x.NormalizedTerm }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Segment, x.Code }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
