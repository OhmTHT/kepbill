# Architecture

## Flow

```
 Angular UI                 .NET API                         Background worker (1 at a time)
 ──────────                 ────────                         ───────────────────────────────
 drop files ──POST──▶ ReceiptService.UploadAsync (companyId, direction)
                       • type/size check, SHA-256 dedupe
                       • save file  (IFileStorage)
                       • insert receipts row  (status=Pending)
                       • enqueue id ───────────────────────▶ ReceiptProcessingService.ProcessAsync
                                                              1. status=Processing
                                                              2. IImagePreprocessor: EXIF rotate, ≤1600px, contrast, JPEG
                                                              3. IReceiptExtractor (Vision | OcrThenLlm) → ExtractedReceipt JSON
                                                                 prompt says which side is our company (ContextHint)
                                                              4. ReceiptMapper: normalize (พ.ศ.→ค.ศ., tax id digits, branch, money)
                                                              5. CompanySide: remember our tax id as printed, then overwrite
                                                                 our side from the company profile; period = issue month
                                                              6. ReceiptValidator (with company + closed periods) → issues
                                                              7. known issuer && issues? → extract again with its
                                                                 few-shot examples (pinned first), keep the better attempt
                                                              8. account = last one used for this counterparty;
                                                                 credit/debit note → link to the referenced invoice
                                                              9. status = Validated (no issues) | NeedsReview | Failed
 poll list / detail ◀─GET─
 edit ──PUT──▶ UpdateAsync (our side re-applied, re-validate)
 type ──POST /{id}/validate──▶ PreviewAsync (no save)
 approve ──POST──▶ ApproveAsync
                     • block on Error unless force, block on closed period
                     • receipt-type document → automatic payment at the issue date
                     • upsert Vendor (issuer) by seller tax id, store approved JSON as VendorExample
 pay ──POST /payments──▶ AddPaymentAsync (blocked when the payment month is closed)
```

## Projects (Clean Architecture)

| Project | Depends on | Contains |
|---|---|---|
| `Domain` | — | `Receipt`, `ReceiptLineItem`, `Payment`, `Company`, `Account`, `ClosedPeriod`, `Vendor`, `VendorExample`, `ValidationIssue`, enums |
| `Application` | Domain, EF Core abstractions | Use cases (`ReceiptService`, `ReceiptProcessingService`, `CompanyService`, `AccountingService`, `LearningService`), `IReceiptExtractor` + prompt + JSON contract, validators (`ThaiTaxId`, `ThaiDateNormalizer`, `ReceiptValidator`), accounting rules (`VatRules`, `JournalBuilder`, `PaymentMath`, `Periods`, `DefaultChart`), ports (`IAppDbContext`, `IFileStorage`, `IProcessingQueue`, `IImagePreprocessor`, `IClock`) |
| `Infrastructure` | Application | EF Core + Npgsql + migrations, Ollama HTTP client, the two extractors, ImageSharp preprocessor, local file storage, `Channel<T>` queue + `BackgroundService` |
| `Api` | Infrastructure | Minimal API endpoints, exception → ProblemDetails, CORS, startup migrate + chart seeding |

All accounting logic (normalizing, validating, VAT, Dr/Cr, payment status) is pure static code in Application and
fully unit-tested without a model or database — that is the part that decides whether a number is trustworthy.

Journal entries are **derived, not stored**: `JournalBuilder` turns approved documents and their payments into
balanced entries on demand. Closing a period is what freezes them (no edits to documents in that period, no payments
dated in it).

## Database (PostgreSQL, snake_case)

```
companies
  id uuid PK, name, tax_id varchar(13), branch, address, vat_registered bool, created_at, updated_at

accounts                                      -- chart of accounts, per company
  id uuid PK, company_id FK (CASCADE), code UNIQUE per company, name,
  type (Asset|Liability|Equity|Revenue|Expense), role (Cash|Bank|AccountsPayable|InputVat|…|None), active

closed_periods
  id uuid PK, company_id FK (CASCADE), period char(7) 'yyyy-MM' UNIQUE per company, closed_at, closed_by

receipts
  id uuid PK
  company_id uuid FK → companies (RESTRICT), direction (Purchase|Sale), period 'yyyy-MM'   IX (company, direction, period)
  original_file_name, stored_path, content_type
  sha256 varchar(64) UNIQUE                   -- duplicate upload guard
  status varchar(20)  IX                      -- Pending | Processing | NeedsReview | Validated | Approved | Failed
  extractor_name, model_name, processing_attempts
  raw_extraction jsonb                        -- what the model returned (for audit / accuracy / fine-tune data)
  ocr_text text                               -- OcrThenLlm mode only
  processing_error text
  issues jsonb                                -- [{code, severity, field, message}]
  document_type varchar(30)                   -- Receipt | AbbreviatedTaxInvoice | FullTaxInvoice | ReceiptTaxInvoice
                                              -- | Invoice | CreditNote | DebitNote | Unknown
  document_number, issue_date date IX, due_date date
  reference_document_number, reference_receipt_id FK → receipts (SET NULL)   -- credit/debit notes
  seller_name, seller_tax_id varchar(13) IX, seller_branch, seller_address
  buyer_name,  buyer_tax_id  varchar(13) IX, buyer_branch,  buyer_address
  own_tax_id_on_document                      -- our tax id as the model read it (mismatch warning only)
  subtotal, discount, vat_amount, total numeric(18,2)
  vat_included bool, currency char(3), payment_method
  account_id FK → accounts (RESTRICT), notes
  vendor_id uuid FK → vendors (SET NULL)
  created_at, processed_at, approved_at, approved_by

receipt_line_items
  id uuid PK, receipt_id uuid FK (CASCADE), line_no, description,
  quantity numeric(18,4), unit_price, discount, amount numeric(18,2)

payments
  id uuid PK, receipt_id FK (CASCADE), date IX, amount numeric(18,2), account_id FK → accounts, note, is_auto, created_at

product_categories
  id uuid PK, company_id FK (CASCADE), name UNIQUE per company, code_prefix varchar(6) UNIQUE per company ('BED6' → BED6123111),
  active (off = kept for its products, not offered for new ones), sort_order, created_at

products
  id uuid PK, company_id FK (CASCADE), category_id FK (RESTRICT), code varchar(30) UNIQUE per company -- set by the user (or AI suggestion), = barcode
  name, unit, sale_price, reorder_point numeric(18,4), track_stock bool, active, created_at, updated_at

product_code_schemes                          -- the questionnaire; none = AI code suggestions off
  id uuid PK, company_id FK (CASCADE) UNIQUE, business_type, product_types text[], notes,
  category_name_hint, product_name_hint, code_hint, segments jsonb [{key, label, length, digits}], updated_at

product_code_terms                            -- learned codes per segment ("brand": Lotus = 001), fed back to the AI
  id uuid PK, company_id FK (CASCADE), segment, term, normalized_term, code varchar(10), created_at
  UNIQUE (company_id, segment, normalized_term), UNIQUE (company_id, segment, code)

warehouses
  id uuid PK, company_id FK (CASCADE), name UNIQUE per company, is_default bool (exactly one per company), active, created_at

stock_movements                               -- the only source of quantity on hand; StockLedger derives qty + moving-average value
  id uuid PK, company_id FK (CASCADE), product_id FK (RESTRICT), warehouse_id FK (RESTRICT), date IX,
  type (Opening|Purchase|Sale|PurchaseReturn|SaleReturn|TransferOut|TransferIn|Adjustment),
  quantity numeric(18,4) (+ in / − out), unit_cost numeric(18,4) (incoming only), note, created_by, created_at

vendors                                       -- document issuers, shared by all companies (layout memory)
  id uuid PK, tax_id varchar(13) UNIQUE, name, branch, approved_receipt_count, created_at, updated_at

vendor_examples
  id uuid PK, vendor_id uuid FK (CASCADE), receipt_id uuid IX, approved_json jsonb, pinned bool, created_at
```

Bookkeeping queries run against `status = 'Approved'` only, e.g. claimable input VAT per period:

```sql
select r.period, sum(r.vat_amount) as input_vat
from receipts r join companies c on c.id = r.company_id
where r.status = 'Approved' and r.direction = 'Purchase' and c.vat_registered
  and r.document_type in ('FullTaxInvoice','ReceiptTaxInvoice','DebitNote')
group by 1 order by 1;          -- credit notes subtract; AccountingService does this for you
```

## Validation rules

Tolerance for arithmetic = ±1.00 THB. **Error** blocks approval (override with `force`), **Warning** only flags.

| Code | Severity | Rule |
|---|---|---|
| `DATE_MISSING` / `DATE_FUTURE` | Error | Date required; not after tomorrow (catches un-converted พ.ศ. years) |
| `DATE_OLD` | Warning | Older than 2 years |
| `SELLER_TAXID_INVALID` / `BUYER_TAXID_INVALID` | Error | 13-digit mod-11 checksum |
| `SELLER_TAXID_MISSING` | Error | Purchase tax invoice/note without the seller's tax id |
| `PROFILE_INCOMPLETE` | Error | Sale tax invoice while our company profile has no tax id |
| `DOC_NUMBER_MISSING` | Error | Required on any tax invoice / credit note / debit note |
| `BUYER_MISSING` | Warning | Sale full tax invoice without the buyer's name |
| `COUNTERPARTY_MISSING` | Warning | No seller name (purchase) / buyer name (sale) |
| `OWN_TAXID_MISMATCH` | Warning | Our tax id printed on the document ≠ company profile (wrong company or side?) |
| `OWN_TAXID_NOT_PRINTED` | Warning | Purchase full tax invoice that doesn't show our tax id (input VAT not claimable) |
| `COUNTERPARTY_IS_SELF` | Warning | Counterparty tax id = our own (wrong side?) |
| `REF_MISSING` | Warning | Credit/debit note without the referenced invoice number |
| `PERIOD_CLOSED` | Error | Unapproved document in a closed period |
| `PERIOD_BEFORE_DATE` | Error | Period earlier than the document's month |
| `PERIOD_LATE` | Warning | Purchase with VAT moved more than 6 months after its month |
| `PERIOD_SALE_MONTH` | Warning | Sale moved out of its own month (output VAT belongs to the issue month) |
| `DUE_BEFORE_ISSUE` | Warning | Due date before the issue date |
| `TOTAL_MISSING` / `TOTAL_ZERO` / `NEGATIVE_AMOUNT` | Error | |
| `TOTAL_MISMATCH` | Error | total ≠ subtotal − discount (+ VAT when exclusive) |
| `VAT_RATE` | Warning | VAT ≠ 7% of base and ≠ 7/107 of total |
| `VAT_MISSING` | Warning | Tax invoice with no VAT |
| `LINE_CALC` | Warning | qty × unit price − line discount ≠ line amount |
| `LINES_MISMATCH` | Warning | Σ lines doesn't match subtotal / total |
| `DOC_TYPE_UNKNOWN` | Warning | |

Date normalization (`ThaiDateNormalizer`): the printed text wins over the model's ISO guess.
4-digit year ≥ 2400 → −543. 2-digit year 60–99 → พ.ศ. 25xx, 00–59 → ค.ศ. 20xx. Thai month names and Thai digits supported.

## Extending

- **PDF**: supported. `ImageSharpPreprocessor` renders the first `Extraction:MaxPdfPages` pages with `PDFtoImage`
  (PDFium) at `Extraction:PdfDpi` and passes one JPEG per page to the extractor, which sends them as several images in
  one Ollama message. Many pages × large images can exceed `Extraction:NumCtx`; raise it if you see "cut off" errors.
- **e-Tax invoice XML**: parse the XML directly — no model needed.
- **Another engine** (e.g. a hosted API, PaddleOCR sidecar): implement `IReceiptExtractor`, register it in
  `Infrastructure/DependencyInjection.cs`, add an `ExtractionMode`.
- **Fine-tune**: `/api/ai/dataset.zip` exports approved documents in chat format; train a vision model, `ollama create`
  it, point `Extraction:VisionModel` at it and compare per-model accuracy on the AI page.
- **Accounting export**: the journal CSV has account codes; map them to Express / PEAK / FlowAccount import formats.
- **Withholding tax (ภาษีหัก ณ ที่จ่าย)**: add a WHT amount on payments and a WHT payable/receivable role in `DefaultChart`.
- **Opening balances**: add a manual journal entry type so the trial balance starts from real balances.
- **Auth**: protect endpoints with Entra ID (`Microsoft.Identity.Web`) and set `ApprovedBy` from the user claim.
- **Scale out**: replace `ChannelProcessingQueue` with Hangfire / RabbitMQ; run several workers against several GPUs.
