export type ReceiptStatus =
  'Pending' | 'Processing' | 'NeedsReview' | 'Validated' | 'Approved' | 'Failed';

export type DocumentType =
  | 'Unknown'
  | 'Receipt'
  | 'AbbreviatedTaxInvoice'
  | 'FullTaxInvoice'
  | 'ReceiptTaxInvoice'
  | 'Invoice'
  | 'CreditNote'
  | 'DebitNote';

export type Direction = 'Purchase' | 'Sale';

export type PaymentStatus = 'NotApplicable' | 'Unpaid' | 'Partial' | 'Paid';

export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Revenue' | 'Expense';

export type AccountRole =
  | 'None'
  | 'Cash'
  | 'Bank'
  | 'AccountsReceivable'
  | 'AccountsPayable'
  | 'InputVat'
  | 'OutputVat'
  | 'SalesRevenue'
  | 'SalesReturns'
  | 'DefaultExpense'
  | 'WhtPayable'
  | 'WhtReceivable';

export interface ValidationIssue {
  code: string;
  severity: 'Warning' | 'Error';
  field: string | null;
  message: string;
}

export interface ReceiptSummary {
  id: string;
  originalFileName: string;
  status: ReceiptStatus;
  direction: Direction;
  period: string | null;
  documentType: DocumentType;
  documentNumber: string | null;
  issueDate: string | null;
  dueDate: string | null;
  counterpartyName: string | null;
  counterpartyTaxId: string | null;
  total: number | null;
  vatAmount: number | null;
  accountName: string | null;
  paymentStatus: PaymentStatus;
  outstanding: number;
  errorCount: number;
  warningCount: number;
  createdAt: string;
}

export interface LineItem {
  lineNo: number;
  description: string;
  quantity: number | null;
  unitPrice: number | null;
  discount: number | null;
  amount: number | null;
}

export interface ReceiptEditable {
  direction: Direction;
  period: string | null;
  documentType: DocumentType;
  documentNumber: string | null;
  issueDate: string | null;
  dueDate: string | null;
  referenceDocumentNumber: string | null;
  sellerName: string | null;
  sellerTaxId: string | null;
  sellerBranch: string | null;
  sellerAddress: string | null;
  buyerName: string | null;
  buyerTaxId: string | null;
  buyerBranch: string | null;
  buyerAddress: string | null;
  subtotal: number | null;
  discount: number | null;
  vatAmount: number | null;
  total: number | null;
  vatIncluded: boolean;
  currency: string | null;
  paymentMethod: string | null;
  accountId: string | null;
  notes: string | null;
  lineItems: LineItem[];
  /** Withholding tax rate in percent; null = none. */
  whtRate: number | null;
  /** Withholding tax in baht (normally the rate × the amount before VAT). */
  whtAmount: number | null;
}

export interface Payment {
  id: string;
  date: string;
  amount: number;
  accountId: string;
  accountName: string | null;
  note: string | null;
  isAuto: boolean;
  /** Withholding tax settled with this payment, on top of the money. */
  whtAmount: number;
}

export interface JournalLine {
  accountId: string;
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

export interface JournalEntry {
  date: string;
  period: string;
  kind: 'document' | 'payment';
  receiptId: string;
  paymentId: string | null;
  documentNumber: string | null;
  description: string;
  lines: JournalLine[];
}

export interface DocumentRef {
  id: string;
  documentNumber: string | null;
  issueDate: string | null;
  total: number | null;
}

export interface ReceiptDetail extends ReceiptEditable {
  id: string;
  companyId: string;
  companyName: string | null;
  periodClosed: boolean;
  originalFileName: string;
  /** Null for a document typed in by hand. */
  fileUrl: string | null;
  contentType: string | null;
  status: ReceiptStatus;
  extractorName: string | null;
  modelName: string | null;
  processingError: string | null;
  issues: ValidationIssue[];
  reference: DocumentRef | null;
  adjustments: DocumentRef[];
  ownTaxIdOnDocument: string | null;
  accountName: string | null;
  vendorId: string | null;
  vendorName: string | null;
  payments: Payment[];
  paid: number;
  adjustmentTotal: number;
  outstanding: number;
  paymentStatus: PaymentStatus;
  journal: JournalEntry[];
  rawExtraction: string | null;
  ocrText: string | null;
  createdAt: string;
  processedAt: string | null;
  approvedAt: string | null;
  approvedBy: string | null;
  /** Pinned as a Training AI example. */
  examplePinned: boolean;
  /** Withholding tax not settled by a payment yet. */
  whtRemaining: number;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UploadResult {
  id: string | null;
  fileName: string;
  duplicate: boolean;
  error: string | null;
}

export interface StatusCount {
  status: ReceiptStatus;
  count: number;
}

export interface Company {
  id: string;
  name: string;
  taxId: string;
  branch: string | null;
  address: string | null;
  vatRegistered: boolean;
  documentCount: number;
  updatedAt: string;
}

export interface CompanyInput {
  name: string;
  taxId: string | null;
  branch: string | null;
  address: string | null;
  vatRegistered: boolean;
}

export interface Account {
  id: string;
  code: string;
  name: string;
  type: AccountType;
  role: AccountRole;
  active: boolean;
  /** Documents booked to it plus payments through it; only unused accounts can be deleted. */
  usage: number;
}

export interface AccountInput {
  code: string;
  name: string;
  type: AccountType;
  active: boolean;
}

export interface ProductCategory {
  id: string;
  name: string;
  /** Where suggested codes of its products start: BED6 → BED6123111. */
  codePrefix: string;
  /** Switched off: its products stay, but new products can't be put in it. */
  active: boolean;
  productCount: number;
}

export interface ProductCategoryInput {
  name: string;
  codePrefix: string;
  /** Omitted on create (a new category is active). */
  active?: boolean;
}

export interface Product {
  id: string;
  categoryId: string;
  categoryName: string;
  /** Set by the user, usually from an AI suggestion (BED6123111); unique per company. */
  code: string;
  name: string;
  unit: string;
  salePrice: number | null;
  /** แจ้งเตือนเมื่อเหลือ (reorder point): running low at or below this quantity. */
  reorderPoint: number | null;
  /** True for goods kept in stock; false for services and fees. */
  trackStock: boolean;
  /** False once no longer sold (เลิกขาย). */
  active: boolean;
  /** Across every warehouse. */
  onHand: number;
  /** Moving average cost per unit. */
  averageCost: number;
  stockValue: number;
  updatedAt: string;
}

export interface WarehouseStock {
  warehouseId: string;
  warehouseName: string;
  active: boolean;
  onHand: number;
  openingQuantity: number | null;
  openingUnitCost: number | null;
}

export interface ProductDetail {
  product: Product;
  /** Date of the ยอดยกมา, or null when there is none. */
  openingDate: string | null;
  warehouses: WarehouseStock[];
}

export interface OpeningStockInput {
  warehouseId: string;
  quantity: number;
  unitCost: number | null;
}

export interface ProductInput {
  categoryId: string;
  name: string;
  unit: string | null;
  salePrice: number | null;
  reorderPoint: number | null;
  trackStock: boolean;
  active: boolean;
  openingDate: string | null;
  /** The whole ยอดยกมา: one row per warehouse that has some. Null leaves it as it is. */
  openings: OpeningStockInput[] | null;
  code: string;
  /** The parts of the AI suggestion the code was taken from, so new ones are learned; null when typed by hand. */
  codeParts: CodePart[] | null;
}

/** One part of a product code after the category prefix: แบรนด์ as 3 digits … */
export interface CodeSegment {
  /** Stable id the learned codes refer to (brand). */
  key: string;
  label: string;
  length: number;
  /** True: 0–9 only; false: A–Z and 0–9. */
  digits: boolean;
}

/** A learned code: แบรนด์ Lotus = 123. */
export interface CodeTerm {
  id: string;
  segment: string;
  term: string;
  code: string;
}

/** The company's naming and code pattern, from the questionnaire. Without one the AI suggestions are off. */
export interface CodeScheme {
  businessType: string;
  productTypes: string[];
  notes: string | null;
  categoryNameHint: string | null;
  productNameHint: string | null;
  codeHint: string | null;
  segments: CodeSegment[];
  terms: CodeTerm[];
  updatedAt: string;
}

export interface SchemeQuestion {
  businessType: string;
  productTypes: string[];
  notes: string | null;
}

export interface CategoryProposal {
  name: string;
  codePrefix: string;
  /** The company already has this category (its own prefix is shown). */
  exists: boolean;
}

export interface SchemeProposal {
  categoryNameHint: string | null;
  productNameHint: string | null;
  codeHint: string | null;
  segments: CodeSegment[];
  categories: CategoryProposal[];
  examples: { name: string; code: string }[];
}

export interface CodeSchemeInput {
  businessType: string;
  productTypes: string[];
  notes: string | null;
  categoryNameHint: string | null;
  productNameHint: string | null;
  codeHint: string | null;
  segments: CodeSegment[];
  newCategories: ProductCategoryInput[];
}

export interface CodePart {
  segment: string;
  /** null = the name has no value for this segment. */
  term: string | null;
  code: string;
}

export interface SuggestedPart extends CodePart {
  label: string;
  /** The company already uses this code for the term. */
  known: boolean;
}

export interface ProductCodeSuggestion {
  code: string;
  parts: SuggestedPart[];
  reason: string | null;
  /** E.g. the code had to be changed to stay unique. */
  warning: string | null;
}

export interface CategoryCodeSuggestion {
  codePrefix: string;
  reason: string | null;
}

export interface Warehouse {
  id: string;
  name: string;
  /** Where documents put stock unless another warehouse is chosen; exactly one per company. */
  isDefault: boolean;
  active: boolean;
}

export interface WarehouseInput {
  name: string;
  isDefault: boolean;
  active: boolean;
}

export interface SideSummary {
  count: number;
  approved: number;
  net: number;
  vat: number;
  total: number;
}

export interface PeriodSummary {
  period: string;
  closed: boolean;
  closedAt: string | null;
  closedBy: string | null;
  purchases: SideSummary;
  sales: SideSummary;
  vatPayable: number;
  pendingCount: number;
  paymentsOut: number;
  paymentsIn: number;
}

export interface Journal {
  period: string;
  entries: JournalEntry[];
  totalDebit: number;
  totalCredit: number;
}

export interface TrialBalanceRow {
  accountId: string;
  code: string;
  name: string;
  type: AccountType;
  periodDebit: number;
  periodCredit: number;
  balanceDebit: number;
  balanceCredit: number;
}

export interface TrialBalance {
  period: string;
  rows: TrialBalanceRow[];
}

export interface OutstandingItem {
  id: string;
  documentNumber: string | null;
  documentType: DocumentType;
  counterpartyName: string | null;
  counterpartyTaxId: string | null;
  issueDate: string | null;
  dueDate: string | null;
  total: number;
  paid: number;
  adjustments: number;
  outstanding: number;
  daysOverdue: number;
  bucket: string;
  /** Withholding tax still to settle (part of outstanding). */
  whtRemaining: number;
}

export interface Outstanding {
  direction: Direction;
  items: OutstandingItem[];
  buckets: Record<string, number>;
  total: number;
}

export interface FieldAccuracy {
  field: string;
  label: string;
  correct: number;
  total: number;
  rate: number;
}

export interface Accuracy {
  documents: number;
  perfect: number;
  fieldRate: number;
  fields: FieldAccuracy[];
  models: { model: string; documents: number; perfect: number; fieldRate: number }[];
  trend: { period: string; documents: number; fieldRate: number }[];
  mostCorrected: {
    id: string;
    originalFileName: string;
    counterpartyName: string | null;
    model: string | null;
    wrongFields: string[];
    approvedAt: string | null;
  }[];
}

export interface Example {
  id: string;
  receiptId: string;
  pinned: boolean;
  createdAt: string;
  documentNumber: string | null;
  issueDate: string | null;
  total: number | null;
}

export interface Issuer {
  id: string;
  taxId: string;
  name: string;
  branch: string | null;
  approvedReceiptCount: number;
  updatedAt: string;
  examples: Example[];
}

export type UserRole = 'User' | 'Approver' | 'Admin' | 'SuperAdmin';

export const USER_ROLE_LABEL: Record<UserRole, string> = {
  SuperAdmin: 'ผู้ดูแลระบบ',
  Admin: 'ผู้ช่วยผู้ดูแลระบบ',
  Approver: 'ผู้ทำบัญชี',
  User: 'ผู้บันทึกบัญชี',
};

/** What each role may do, shown when choosing one. */
export const USER_ROLE_HINT: Record<UserRole, string> = {
  SuperAdmin: 'ดูแลทั้งระบบ เห็นทุกบริษัท ใช้ได้ทุกฟีเจอร์ รวมถึงเพิ่ม/ลบบริษัทและ Training AI',
  Admin: 'ดูแลบริษัทที่ได้รับมอบหมาย: ผังบัญชี และผู้ใช้ระดับผู้ทำบัญชี/ผู้บันทึก — เพิ่มหรือแก้โปรไฟล์บริษัทไม่ได้',
  Approver: 'บันทึกและอนุมัติเอกสาร บันทึกการชำระ ปิดงวด ดูสมุดรายวันและค้างชำระ ของบริษัทที่ได้รับมอบหมาย',
  User: 'บันทึกเอกสารรายรับ/รายจ่ายเท่านั้น อนุมัติไม่ได้',
};

export const USER_ROLE_COLOR: Record<UserRole, string> = {
  SuperAdmin: 'green',
  Admin: 'blue',
  Approver: 'cyan',
  User: 'default',
};

/** Roles a user may hand out to the users they manage (mirrors the API's RoleRules.Assignable), highest first. */
export function assignableRoles(actor: UserRole | undefined): UserRole[] {
  if (actor === 'SuperAdmin') return ['SuperAdmin', 'Admin', 'Approver', 'User'];
  if (actor === 'Admin') return ['Approver', 'User'];
  return [];
}

export interface User {
  id: string;
  /** ชื่อผู้ใช้งาน: shown in the app and used to sign in. */
  name: string;
  role: UserRole;
  active: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  /** Companies a member may open; empty for admins, who can open every company. */
  companyIds: string[];
}

export interface AuthStatus {
  /** No users exist yet: the first visitor creates the admin. */
  setupRequired: boolean;
  user: User | null;
}

export interface UserInput {
  name: string;
  password: string;
  role: UserRole;
  companyIds: string[];
}

export interface UserUpdate {
  name: string;
  role: UserRole;
  active: boolean;
  companyIds: string[];
}

export const STATUS_LABEL: Record<ReceiptStatus, string> = {
  Pending: 'รอคิว',
  Processing: 'กำลังอ่าน',
  NeedsReview: 'รอตรวจสอบ',
  Validated: 'รออนุมัติ',
  Approved: 'อนุมัติแล้ว',
  Failed: 'ล้มเหลว',
};

export const DOC_TYPE_LABEL: Record<DocumentType, string> = {
  Unknown: 'ไม่ทราบ',
  Receipt: 'ใบเสร็จรับเงิน',
  AbbreviatedTaxInvoice: 'ใบกำกับภาษีอย่างย่อ',
  FullTaxInvoice: 'ใบกำกับภาษีเต็มรูป',
  ReceiptTaxInvoice: 'ใบเสร็จ/ใบกำกับภาษี',
  Invoice: 'ใบแจ้งหนี้/ใบวางบิล',
  CreditNote: 'ใบลดหนี้',
  DebitNote: 'ใบเพิ่มหนี้',
};

/** Common withholding tax rates (paid to Thai companies/residents); the amount can still be typed by hand. */
export const WHT_RATES: { rate: number; label: string }[] = [
  { rate: 1, label: 'ค่าขนส่ง / เบี้ยประกันวินาศภัย' },
  { rate: 2, label: 'ค่าโฆษณา' },
  { rate: 3, label: 'ค่าบริการ / จ้างทำของ / วิชาชีพ' },
  { rate: 5, label: 'ค่าเช่า / รางวัล ส่งเสริมการขาย' },
  { rate: 10, label: 'เงินปันผล' },
  { rate: 15, label: 'ผู้รับอยู่ต่างประเทศ' },
];

export const DIRECTION_LABEL: Record<Direction, string> = {
  Purchase: 'รายจ่าย',
  Sale: 'รายรับ',
};

export const COUNTERPARTY_LABEL: Record<Direction, string> = {
  Purchase: 'ผู้ขาย',
  Sale: 'ผู้ซื้อ',
};

export const PAYMENT_STATUS_LABEL: Record<Direction, Record<PaymentStatus, string>> = {
  Purchase: { NotApplicable: '—', Unpaid: 'ค้างจ่าย', Partial: 'จ่ายบางส่วน', Paid: 'จ่ายแล้ว' },
  Sale: { NotApplicable: '—', Unpaid: 'ค้างรับ', Partial: 'รับบางส่วน', Paid: 'รับแล้ว' },
};

export const ACCOUNT_TYPE_LABEL: Record<AccountType, string> = {
  Asset: 'สินทรัพย์',
  Liability: 'หนี้สิน',
  Equity: 'ทุน',
  Revenue: 'รายได้',
  Expense: 'ค่าใช้จ่าย',
};

export const ACCOUNT_ROLE_LABEL: Record<AccountRole, string> = {
  None: '',
  Cash: 'เงินสด',
  Bank: 'ธนาคาร',
  AccountsReceivable: 'ลูกหนี้',
  AccountsPayable: 'เจ้าหนี้',
  InputVat: 'ภาษีซื้อ',
  OutputVat: 'ภาษีขาย',
  SalesRevenue: 'รายได้หลัก',
  SalesReturns: 'รับคืน',
  DefaultExpense: 'ค่าใช้จ่ายเริ่มต้น',
  WhtPayable: 'หัก ณ ที่จ่ายค้างจ่าย',
  WhtReceivable: 'ถูกหัก ณ ที่จ่าย',
};

/** nz-tag colors per status. */
export const STATUS_COLOR: Record<ReceiptStatus, string> = {
  Pending: 'default',
  Processing: 'processing',
  NeedsReview: 'warning',
  Validated: 'green',
  Approved: '#1a7a56',
  Failed: 'error',
};

export const DIRECTION_COLOR: Record<Direction, string> = {
  Purchase: 'blue',
  Sale: 'green',
};

const THAI_MONTHS = [
  'ม.ค.',
  'ก.พ.',
  'มี.ค.',
  'เม.ย.',
  'พ.ค.',
  'มิ.ย.',
  'ก.ค.',
  'ส.ค.',
  'ก.ย.',
  'ต.ค.',
  'พ.ย.',
  'ธ.ค.',
];

/** "2026-10" → "ต.ค. 2569" */
export function periodLabel(period: string | null | undefined): string {
  if (!period) return '—';
  const [y, m] = period.split('-').map(Number);
  if (!y || !m) return period;
  return `${THAI_MONTHS[m - 1]} ${y + 543}`;
}

/** The current month as "yyyy-MM" in local time. */
export function currentPeriod(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** "yyyy-MM-dd" or "yyyy-MM" → local Date (for nz-date-picker), or null. */
export function toDate(value: string | null | undefined): Date | null {
  if (!value) return null;
  const [y, m, d] = value.split('-').map(Number);
  return y && m ? new Date(y, m - 1, d || 1) : null;
}

/** Date → "yyyy-MM-dd" in local time, or null. */
export function fromDate(d: Date | null | undefined): string | null {
  return d ? `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` : null;
}

/** Date → "yyyy-MM" in local time, or null. */
export function fromMonth(d: Date | null | undefined): string | null {
  return d ? `${d.getFullYear()}-${pad(d.getMonth() + 1)}` : null;
}

/** Today as "yyyy-MM-dd" in local time. */
export function today(): string {
  const d = new Date();
  return `${currentPeriod()}-${String(d.getDate()).padStart(2, '0')}`;
}
