using ReceiptReader.Application.Validation;
using ReceiptReader.Domain.Entities;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Companies;

/// <summary>Our side of a document always comes from the company profile, never from the model or the form.</summary>
public static class CompanySide
{
    /// <summary>Remember what the document printed for our side (for the mismatch warning), then overwrite it.</summary>
    public static void CaptureAndApply(Receipt r, Company c)
    {
        r.OwnTaxIdOnDocument = ThaiTaxId.Normalize(r.Direction == Direction.Purchase ? r.BuyerTaxId : r.SellerTaxId);
        Apply(r, c);
    }

    public static void Apply(Receipt r, Company c)
    {
        var taxId = string.IsNullOrWhiteSpace(c.TaxId) ? null : c.TaxId;
        if (r.Direction == Direction.Purchase)
        {
            r.BuyerName = c.Name;
            r.BuyerTaxId = taxId;
            r.BuyerBranch = c.Branch;
            r.BuyerAddress = c.Address;
        }
        else
        {
            r.SellerName = c.Name;
            r.SellerTaxId = taxId;
            r.SellerBranch = c.Branch;
            r.SellerAddress = c.Address;
        }
    }
}
