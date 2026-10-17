using System.Text;
using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Domain.Entities;

namespace ReceiptReader.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptLineItem> ReceiptLineItems => Set<ReceiptLineItem>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<VendorExample> VendorExamples => Set<VendorExample>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ClosedPeriod> ClosedPeriods => Set<ClosedPeriod>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCompany> UserCompanies => Set<UserCompany>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ProductCodeScheme> ProductCodeSchemes => Set<ProductCodeScheme>();
    public DbSet<ProductCodeTerm> ProductCodeTerms => Set<ProductCodeTerm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplySnakeCase(modelBuilder);
    }

    /// <summary>receipts.seller_tax_id instead of "Receipts"."SellerTaxId" — friendlier for hand-written SQL.</summary>
    private static void ApplySnakeCase(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table) entity.SetTableName(ToSnake(table));
            foreach (var p in entity.GetProperties()) p.SetColumnName(ToSnake(p.GetColumnName()));
            foreach (var k in entity.GetKeys())
                if (k.GetName() is { } kn) k.SetName(ToSnake(kn));
            foreach (var fk in entity.GetForeignKeys())
                if (fk.GetConstraintName() is { } fn) fk.SetConstraintName(ToSnake(fn));
            foreach (var ix in entity.GetIndexes())
                if (ix.GetDatabaseName() is { } ixn) ix.SetDatabaseName(ToSnake(ixn));
        }
    }

    internal static string ToSnake(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && (char.IsLower(name[i - 1]) ||
                    (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
