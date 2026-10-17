using System.Text;
using System.Text.Json.Nodes;

namespace ReceiptReader.Application.Extraction;

/// <summary>Prompt text and JSON schema shared by every extractor.</summary>
public static class ReceiptPrompt
{
    public const string SystemPrompt =
        """
        You are a meticulous Thai bookkeeping assistant. You read receipts and tax invoices
        (ใบเสร็จรับเงิน / ใบกำกับภาษี) and return ONLY a JSON object that matches the schema.

        Rules:
        - Copy text exactly as printed (Thai or English). Never translate or invent values.
        - If a value is not printed or unreadable, use null. Do not guess.
        - Numbers: plain numbers without commas or currency symbols (1,234.50 → 1234.5).
        - issue_date_raw: the date exactly as printed. issue_date: the same date as YYYY-MM-DD,
          keeping the year as printed (Thai Buddhist-era years like 2569 are fine; the system converts them).
        - seller_tax_id / buyer_tax_id: the 13-digit เลขประจำตัวผู้เสียภาษี, digits only.
        - seller_branch / buyer_branch: "00000" for สำนักงานใหญ่ / Head Office, otherwise the branch number.
          null if not printed.
        - document_type:
            "full_tax_invoice"        = ใบกำกับภาษี (เต็มรูป) with buyer name/address
            "abbreviated_tax_invoice" = ใบกำกับภาษีอย่างย่อ (typical POS slip with TAX ID / POS ID)
            "receipt_tax_invoice"     = ใบเสร็จรับเงิน/ใบกำกับภาษี combined document
            "receipt"                 = ใบเสร็จรับเงิน without tax invoice wording
            "invoice"                 = ใบแจ้งหนี้ / ใบวางบิล (a bill, not a tax invoice)
            "credit_note"             = ใบลดหนี้ / Credit Note
            "debit_note"              = ใบเพิ่มหนี้ / Debit Note
            "unknown"                 = cannot tell
        - reference_document_number: on a credit/debit note, the number of the original tax invoice
          it refers to (อ้างถึงใบกำกับภาษีเลขที่). null on other documents.
        - due_date: the payment due date (วันครบกำหนด / ครบกำหนดชำระ / Due date) as YYYY-MM-DD, or null.
        - items: one entry per purchased line. discount = the discount printed on that line in baht
          (a positive number; null if none). amount = the line total as printed, after the line discount.
        - Amounts on credit/debit notes are the amounts of the adjustment itself, as positive numbers.
        - subtotal: total before VAT and discount if printed, otherwise null.
        - vat_amount: VAT (ภาษีมูลค่าเพิ่ม) in baht. vat_included: true if item prices already include VAT.
        - total: the final amount paid (ยอดสุทธิ / จำนวนเงินรวมทั้งสิ้น / TOTAL).
        """;

    public const string UserInstructionForImage =
        "Extract the receipt in this image into the JSON schema. " +
        "If there are several images, they are consecutive pages of one document.";

    public static string UserInstructionForOcrText(string ocrText) =>
        $"""
        Extract the receipt below into the JSON schema. The text came from OCR and may contain
        small recognition errors; use the layout and arithmetic to resolve them.

        <ocr_text>
        {ocrText}
        </ocr_text>
        """;

    /// <summary>
    /// Tells the model which side of the document is our company. Our side is filled in from the company
    /// profile afterwards, so the model should spend its attention on the counterparty.
    /// </summary>
    public static string ContextHint(ExtractionContext? ctx)
    {
        if (ctx is null) return "";
        var (ourRole, theirRole, theirPrefix) = ctx.IsPurchase
            ? ("BUYER", "SELLER", "seller_")
            : ("SELLER", "BUYER", "buyer_");
        var taxId = string.IsNullOrWhiteSpace(ctx.CompanyTaxId) ? "" : $" (tax id {ctx.CompanyTaxId})";
        return $"""


            Context: this document was filed as a {(ctx.IsPurchase ? "PURCHASE" : "SALE")} of our company "{ctx.CompanyName}"{taxId}.
            Our company is the {ourRole}; its details are already known. Concentrate on the {theirRole} (the
            counterparty): copy {theirPrefix}name, {theirPrefix}tax_id, {theirPrefix}branch and {theirPrefix}address exactly as printed.
            Still fill the {ourRole.ToLowerInvariant()}_* fields with what is printed (or null) — they are only used to check
            that the document really is addressed to our company.
            """;
    }

    /// <summary>Appended when we already know this seller: earlier approved receipts as format hints.</summary>
    public static string VendorHint(IReadOnlyList<string> examples)
    {
        if (examples.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("This seller has been seen before. Below are correct extractions of earlier receipts");
        sb.AppendLine("from the same seller. Use them to understand the layout and field placement only;");
        sb.AppendLine("the values on the new receipt (date, number, items, amounts) will be different.");
        for (var i = 0; i < examples.Count; i++)
        {
            sb.AppendLine($"<example index=\"{i + 1}\">");
            sb.AppendLine(examples[i]);
            sb.AppendLine("</example>");
        }
        return sb.ToString();
    }

    public const string OcrPrompt =
        "Transcribe all text in this receipt image exactly as printed, line by line, keeping the " +
        "reading order and the column layout (use spaces between columns). Do not summarize or translate. " +
        "If there are several images, they are consecutive pages: transcribe them in order.";

    private static JsonObject Nullable(string type) => new() { ["type"] = new JsonArray(type, "null") };

    /// <summary>JSON schema passed to Ollama's <c>format</c> field (structured outputs).</summary>
    public static JsonObject JsonSchema()
    {
        var item = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["description"] = Nullable("string"),
                ["quantity"] = Nullable("number"),
                ["unit_price"] = Nullable("number"),
                ["discount"] = Nullable("number"),
                ["amount"] = Nullable("number"),
            },
            ["required"] = new JsonArray("description", "quantity", "unit_price", "discount", "amount"),
        };

        var props = new JsonObject
        {
            ["document_type"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("receipt", "abbreviated_tax_invoice", "full_tax_invoice",
                                          "receipt_tax_invoice", "invoice", "credit_note", "debit_note", "unknown"),
            },
            ["document_number"] = Nullable("string"),
            ["reference_document_number"] = Nullable("string"),
            ["issue_date_raw"] = Nullable("string"),
            ["issue_date"] = Nullable("string"),
            ["due_date"] = Nullable("string"),
            ["seller_name"] = Nullable("string"),
            ["seller_tax_id"] = Nullable("string"),
            ["seller_branch"] = Nullable("string"),
            ["seller_address"] = Nullable("string"),
            ["buyer_name"] = Nullable("string"),
            ["buyer_tax_id"] = Nullable("string"),
            ["buyer_branch"] = Nullable("string"),
            ["buyer_address"] = Nullable("string"),
            ["items"] = new JsonObject { ["type"] = "array", ["items"] = item },
            ["subtotal"] = Nullable("number"),
            ["discount"] = Nullable("number"),
            ["vat_amount"] = Nullable("number"),
            ["vat_included"] = Nullable("boolean"),
            ["total"] = Nullable("number"),
            ["currency"] = Nullable("string"),
            ["payment_method"] = Nullable("string"),
        };

        var required = new JsonArray();
        foreach (var key in props.Select(p => p.Key)) required.Add(key);

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required,
        };
    }
}
