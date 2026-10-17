using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;

namespace ReceiptReader.Application.Users;

/// <summary>Which companies a signed-in user may work with: admins every company, members only those linked to them.</summary>
public sealed class CompanyAccess(IAppDbContext db)
{
    /// <summary>The user's assigned companies, or null for a super admin (no restriction).</summary>
    public async Task<IReadOnlyCollection<Guid>?> AllowedCompaniesAsync(Guid userId, bool seesAll, CancellationToken ct) =>
        seesAll ? null : await db.UserCompanies.Where(uc => uc.UserId == userId).Select(uc => uc.CompanyId).ToListAsync(ct);

    public async Task<bool> CanOpenCompanyAsync(Guid userId, bool seesAll, Guid companyId, CancellationToken ct) =>
        seesAll || await db.UserCompanies.AnyAsync(uc => uc.UserId == userId && uc.CompanyId == companyId, ct);

    /// <summary>The company a document belongs to, or null if there is no such document.</summary>
    public async Task<Guid?> CompanyOfReceiptAsync(Guid receiptId, CancellationToken ct) =>
        await db.Receipts.Where(r => r.Id == receiptId).Select(r => (Guid?)r.CompanyId).FirstOrDefaultAsync(ct);
}
