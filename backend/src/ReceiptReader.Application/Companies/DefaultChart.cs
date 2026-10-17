using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Companies;

/// <summary>A small Thai SME chart of accounts seeded into every new company. Users can rename and extend it.</summary>
public static class DefaultChart
{
    private static readonly (string Code, string Name, AccountType Type, AccountRole Role)[] Rows =
    [
        ("1111", "เงินสด", AccountType.Asset, AccountRole.Cash),
        ("1112", "เงินฝากธนาคาร", AccountType.Asset, AccountRole.Bank),
        ("1131", "ลูกหนี้การค้า", AccountType.Asset, AccountRole.AccountsReceivable),
        ("1153", "ภาษีเงินได้ถูกหัก ณ ที่จ่าย", AccountType.Asset, AccountRole.WhtReceivable),
        ("1154", "ภาษีซื้อ", AccountType.Asset, AccountRole.InputVat),
        ("2111", "เจ้าหนี้การค้า", AccountType.Liability, AccountRole.AccountsPayable),
        ("2153", "ภาษีเงินได้หัก ณ ที่จ่ายค้างจ่าย", AccountType.Liability, AccountRole.WhtPayable),
        ("2154", "ภาษีขาย", AccountType.Liability, AccountRole.OutputVat),
        ("3100", "ทุน", AccountType.Equity, AccountRole.None),
        ("4111", "รายได้จากการขาย", AccountType.Revenue, AccountRole.SalesRevenue),
        ("4112", "รายได้จากการให้บริการ", AccountType.Revenue, AccountRole.None),
        ("4113", "รับคืนและส่วนลดจ่าย", AccountType.Revenue, AccountRole.SalesReturns),
        ("5111", "ซื้อสินค้า", AccountType.Expense, AccountRole.None),
        ("5210", "ค่าใช้จ่ายทั่วไป", AccountType.Expense, AccountRole.DefaultExpense),
        ("5211", "ค่าเดินทาง", AccountType.Expense, AccountRole.None),
        ("5212", "ค่าน้ำมันเชื้อเพลิง", AccountType.Expense, AccountRole.None),
        ("5213", "ค่ารับรองและอาหาร", AccountType.Expense, AccountRole.None),
        ("5214", "ค่าวัสดุสิ้นเปลืองและเครื่องเขียน", AccountType.Expense, AccountRole.None),
        ("5215", "ค่าสาธารณูปโภค", AccountType.Expense, AccountRole.None),
        ("5216", "ค่าซ่อมแซมและบำรุงรักษา", AccountType.Expense, AccountRole.None),
        ("5217", "ค่าเช่า", AccountType.Expense, AccountRole.None),
        ("5218", "ค่าบริการวิชาชีพ", AccountType.Expense, AccountRole.None),
        ("5219", "ค่าธรรมเนียมธนาคาร", AccountType.Expense, AccountRole.None),
        ("5220", "ค่าโฆษณา", AccountType.Expense, AccountRole.None),
    ];

    public static List<Account> Create(Guid companyId) =>
        Rows.Select(r => new Account { CompanyId = companyId, Code = r.Code, Name = r.Name, Type = r.Type, Role = r.Role })
            .ToList();

    /// <summary>
    /// The full chart for a company without accounts (created before charts existed); otherwise only accounts
    /// for the roles it is missing (a role added in a later version).
    /// </summary>
    public static List<Account> Missing(Guid companyId, IReadOnlyCollection<Account> existing)
    {
        if (existing.Count == 0) return Create(companyId);
        var roles = existing.Select(a => a.Role).ToHashSet();
        var codes = existing.Select(a => a.Code).ToHashSet();
        return Rows.Where(r => r.Role != AccountRole.None && !roles.Contains(r.Role))
            .Select(r => new Account
            {
                CompanyId = companyId,
                Code = codes.Contains(r.Code) ? r.Code + "-1" : r.Code,
                Name = r.Name,
                Type = r.Type,
                Role = r.Role,
            })
            .ToList();
    }
}
