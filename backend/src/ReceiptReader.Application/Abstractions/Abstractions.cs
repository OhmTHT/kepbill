using Microsoft.EntityFrameworkCore;
using ReceiptReader.Domain.Entities;

namespace ReceiptReader.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Receipt> Receipts { get; }
    DbSet<ReceiptLineItem> ReceiptLineItems { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<VendorExample> VendorExamples { get; }
    DbSet<Company> Companies { get; }
    DbSet<Account> Accounts { get; }
    DbSet<ClosedPeriod> ClosedPeriods { get; }
    DbSet<Payment> Payments { get; }
    DbSet<User> Users { get; }
    DbSet<UserCompany> UserCompanies { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Product> Products { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<ProductCodeScheme> ProductCodeSchemes { get; }
    DbSet<ProductCodeTerm> ProductCodeTerms { get; }
    /// <summary>For operations that must be all-or-nothing across several bulk statements.</summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface IFileStorage
{
    /// <summary>Saves the stream and returns a storage-relative path.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct);
    Stream OpenRead(string storedPath);
}

/// <summary>Turns any supported upload into model-friendly JPEGs (auto-rotated, size-capped): one per page.</summary>
public interface IImagePreprocessor
{
    Task<IReadOnlyList<byte[]>> PrepareAsync(Stream source, CancellationToken ct);
}

/// <summary>Background queue of receipt ids waiting for extraction.</summary>
public interface IProcessingQueue
{
    ValueTask EnqueueAsync(Guid receiptId, CancellationToken ct = default);
}

public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly TodayInThailand { get; }
}

/// <summary>A text model that answers with JSON matching a schema. Used for the product code suggestions.</summary>
public interface ILanguageModel
{
    Task<string> CompleteJsonAsync(string system, string user, System.Text.Json.Nodes.JsonObject schema, CancellationToken ct);
}
