using Microsoft.EntityFrameworkCore;
using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Accounting;
using ReceiptReader.Application.Inventory;
using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Companies;

public sealed record CompanyDto(
    Guid Id, string Name, string TaxId, string? Branch, string? Address, bool VatRegistered,
    int DocumentCount, DateTimeOffset UpdatedAt);

public sealed record CompanyInput(string Name, string? TaxId, string? Branch, string? Address, bool VatRegistered);

/// <param name="Usage">Documents booked to the account plus payments made through it. Only unused accounts can be deleted.</param>
public sealed record AccountDto(Guid Id, string Code, string Name, AccountType Type, AccountRole Role, bool Active, int Usage);

public sealed record AccountInput(string Code, string Name, AccountType Type, bool Active = true);

/// <summary>Everything about a company that the document, accounting and validation code needs.</summary>
public sealed class CompanyContext(Company company, IReadOnlyList<Account> accounts, IReadOnlySet<string> closedPeriods)
{
    public Company Company { get; } = company;
    public IReadOnlyList<Account> Accounts { get; } = accounts;
    public ChartLookup Chart { get; } = new(accounts);
    public IReadOnlySet<string> ClosedPeriods { get; } = closedPeriods;
    public ValidationContext Validation { get; } = ValidationContext.For(company, closedPeriods);

    public bool IsClosed(string? period) => period is not null && ClosedPeriods.Contains(period);
}

public static class CompanyContextLoader
{
    public static async Task<CompanyContext> LoadCompanyContextAsync(this IAppDbContext db, Guid companyId, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct)
                      ?? throw new NotFoundException($"ไม่พบบริษัท {companyId}");
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct);
        var closed = await db.ClosedPeriods.AsNoTracking().Where(p => p.CompanyId == companyId)
            .Select(p => p.Period).ToListAsync(ct);
        return new CompanyContext(company, accounts, closed.ToHashSet());
    }
}

/// <summary>Company profiles and their charts of accounts.</summary>
public sealed class CompanyService(IAppDbContext db, IClock clock)
{
    /// <param name="only">Restrict to these companies (a member's); null lists every company.</param>
    public async Task<IReadOnlyList<CompanyDto>> ListAsync(CancellationToken ct, IReadOnlyCollection<Guid>? only = null) =>
        await db.Companies.AsNoTracking()
            .Where(c => only == null || only.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new CompanyDto(c.Id, c.Name, c.TaxId, c.Branch, c.Address, c.VatRegistered,
                db.Receipts.Count(r => r.CompanyId == c.Id), c.UpdatedAt))
            .ToListAsync(ct);

    public async Task<CompanyDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException($"ไม่พบบริษัท {id}");

    public async Task<CompanyDto> CreateAsync(CompanyInput input, CancellationToken ct)
    {
        var c = new Company { CreatedAt = clock.Now };
        await ApplyAsync(input, c, ct);
        c.Accounts = DefaultChart.Create(c.Id);
        db.Companies.Add(c);
        db.Warehouses.Add(InventoryService.DefaultWarehouse(c.Id));
        await db.SaveChangesAsync(ct);
        return await GetAsync(c.Id, ct);
    }

    public async Task<CompanyDto> UpdateAsync(Guid id, CompanyInput input, CancellationToken ct)
    {
        var c = await db.Companies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException($"ไม่พบบริษัท {id}");
        await ApplyAsync(input, c, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Deletes the company with everything booked under it — documents (with their lines and payments), chart of
    /// accounts, closed periods, AI examples taken from its documents, products with their stock, warehouses and user links — all or nothing.
    /// Uploaded files stay on disk, as they do when a single document is deleted.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException($"ไม่พบบริษัท {id}");
        var receiptIds = db.Receipts.Where(r => r.CompanyId == id).Select(r => r.Id);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.VendorExamples.Where(e => receiptIds.Contains(e.ReceiptId)).ExecuteDeleteAsync(ct);
        // Line items and payments go with their documents (cascade); accounts, closed periods and user links with the company.
        await db.Receipts.Where(r => r.CompanyId == id).ExecuteDeleteAsync(ct);
        // Stock movements hold their product and warehouse back, and products their category (restrict);
        // categories and warehouses then go with the company.
        await db.StockMovements.Where(m => m.CompanyId == id).ExecuteDeleteAsync(ct);
        await db.Products.Where(p => p.CompanyId == id).ExecuteDeleteAsync(ct);
        db.Companies.Remove(company);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Seed the default chart for companies that don't have every system account yet. Idempotent.</summary>
    public async Task EnsureChartsAsync(CancellationToken ct)
    {
        var companies = await db.Companies.Include(c => c.Accounts).ToListAsync(ct);
        foreach (var c in companies)
            foreach (var a in DefaultChart.Missing(c.Id, c.Accounts))
                db.Accounts.Add(a);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- accounts ----------------

    public async Task<IReadOnlyList<AccountDto>> ListAccountsAsync(Guid companyId, CancellationToken ct) =>
        await db.Accounts.AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .Select(a => new AccountDto(a.Id, a.Code, a.Name, a.Type, a.Role, a.Active,
                db.Receipts.Count(r => r.AccountId == a.Id) + db.Payments.Count(p => p.AccountId == a.Id)))
            .ToListAsync(ct);

    public async Task<AccountDto> CreateAccountAsync(Guid companyId, AccountInput input, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(c => c.Id == companyId, ct)) throw new NotFoundException($"ไม่พบบริษัท {companyId}");
        var a = new Account { CompanyId = companyId, Role = AccountRole.None };
        await ApplyAsync(input, a, ct);
        db.Accounts.Add(a);
        await db.SaveChangesAsync(ct);
        return new AccountDto(a.Id, a.Code, a.Name, a.Type, a.Role, a.Active, 0);
    }

    public async Task<AccountDto> UpdateAccountAsync(Guid companyId, Guid accountId, AccountInput input, CancellationToken ct)
    {
        var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && x.CompanyId == companyId, ct)
                ?? throw new NotFoundException($"ไม่พบบัญชี {accountId}");
        if (a.Role != AccountRole.None)
        {
            // System accounts keep their type and can't be switched off: the journal generator posts to them.
            if (input.Type != a.Type) throw new BadRequestException("เปลี่ยนประเภทของบัญชีระบบไม่ได้");
            if (!input.Active) throw new BadRequestException("ปิดใช้บัญชีระบบไม่ได้");
        }
        await ApplyAsync(input, a, ct);
        await db.SaveChangesAsync(ct);
        return new AccountDto(a.Id, a.Code, a.Name, a.Type, a.Role, a.Active, await UsageAsync(a.Id, ct));
    }

    /// <summary>
    /// Remove an account added by mistake. The journal is built from the documents and payments that point at an
    /// account, so one that has been used would change past periods if removed; those can only be switched off.
    /// </summary>
    public async Task DeleteAccountAsync(Guid companyId, Guid accountId, CancellationToken ct)
    {
        var a = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && x.CompanyId == companyId, ct)
                ?? throw new NotFoundException($"ไม่พบบัญชี {accountId}");
        if (a.Role != AccountRole.None) throw new BadRequestException("ลบบัญชีระบบไม่ได้");
        var receipts = await db.Receipts.CountAsync(r => r.AccountId == a.Id, ct);
        var payments = await db.Payments.CountAsync(p => p.AccountId == a.Id, ct);
        if (receipts + payments > 0)
        {
            var used = string.Join(" และ", new[] { receipts > 0 ? $"เอกสาร {receipts} ใบ" : null, payments > 0 ? $"การชำระเงิน {payments} รายการ" : null }
                .Where(x => x is not null));
            throw new ConflictException($"บัญชี {a.Code} {a.Name} ถูกใช้ใน{used} ลบไม่ได้ — ปิดใช้แทน");
        }
        db.Accounts.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> UsageAsync(Guid accountId, CancellationToken ct) =>
        await db.Receipts.CountAsync(r => r.AccountId == accountId, ct)
        + await db.Payments.CountAsync(p => p.AccountId == accountId, ct);

    // ------------------------------------------------------------------

    private async Task ApplyAsync(CompanyInput input, Company c, CancellationToken ct)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw new BadRequestException("กรอกชื่อบริษัท");
        var taxId = ThaiTaxId.Normalize(input.TaxId) ?? "";
        if (taxId.Length > 0 && !ThaiTaxId.IsValid(taxId))
            throw new BadRequestException("เลขผู้เสียภาษีไม่ถูกต้อง (checksum ไม่ผ่าน)");
        var branch = ThaiBranch.Normalize(input.Branch);
        if (taxId.Length > 0 && await db.Companies.AnyAsync(x => x.TaxId == taxId && x.Branch == branch && x.Id != c.Id, ct))
            throw new ConflictException("มีบริษัทเลขผู้เสียภาษีและสาขานี้อยู่แล้ว");

        c.Name = name.Length > 300 ? name[..300] : name;
        c.TaxId = taxId;
        c.Branch = branch;
        c.Address = string.IsNullOrWhiteSpace(input.Address) ? null : input.Address.Trim();
        c.VatRegistered = input.VatRegistered;
        c.UpdatedAt = clock.Now;
    }

    private async Task ApplyAsync(AccountInput input, Account a, CancellationToken ct)
    {
        var code = input.Code?.Trim();
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) throw new BadRequestException("กรอกรหัสและชื่อบัญชี");
        if (code.Length > 20) throw new BadRequestException("รหัสบัญชียาวเกิน 20 ตัวอักษร");
        if (await db.Accounts.AnyAsync(x => x.CompanyId == a.CompanyId && x.Code == code && x.Id != a.Id, ct))
            throw new ConflictException($"มีบัญชีรหัส {code} อยู่แล้ว");
        a.Code = code;
        a.Name = name.Length > 200 ? name[..200] : name;
        a.Type = input.Type;
        a.Active = input.Active;
    }
}
