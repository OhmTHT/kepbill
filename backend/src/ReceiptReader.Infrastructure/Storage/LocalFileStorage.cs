using Microsoft.Extensions.Options;
using ReceiptReader.Application.Abstractions;

namespace ReceiptReader.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    public string RootPath { get; set; } = "data/uploads";
}

/// <summary>Stores uploads under RootPath/yyyy/MM/{guid}.ext. Swap for Azure Blob / S3 later via IFileStorage.</summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var relative = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), $"{Guid.NewGuid():N}{extension}");
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fs, ct);
        return relative.Replace('\\', '/');
    }

    public Stream OpenRead(string storedPath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storedPath));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid storage path");
        if (!File.Exists(full))
            throw new FileNotFoundException("Stored receipt file is missing", storedPath);
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    }
}
