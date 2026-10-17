using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Domain.Entities;

/// <summary>A single rule violation found on a receipt. Stored as jsonb on the receipt row.</summary>
/// <param name="Code">Stable machine code, e.g. TOTAL_MISMATCH.</param>
/// <param name="Field">The field the UI should highlight (camelCase), or null for whole-document issues.</param>
/// <param name="Message">Human-readable message (Thai).</param>
public sealed record ValidationIssue(string Code, IssueSeverity Severity, string? Field, string Message);
