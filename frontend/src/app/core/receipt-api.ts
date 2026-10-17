import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Account,
  AccountInput,
  Accuracy,
  CategoryCodeSuggestion,
  CodeScheme,
  CodeSchemeInput,
  Company,
  CompanyInput,
  Direction,
  Issuer,
  Journal,
  Outstanding,
  PagedResult,
  PeriodSummary,
  Product,
  ProductCategory,
  ProductCategoryInput,
  ProductDetail,
  ProductCodeSuggestion,
  ProductInput,
  ReceiptDetail,
  ReceiptEditable,
  ReceiptStatus,
  ReceiptSummary,
  SchemeProposal,
  SchemeQuestion,
  StatusCount,
  TrialBalance,
  UploadResult,
  User,
  UserInput,
  UserUpdate,
  ValidationIssue,
  Warehouse,
  WarehouseInput,
} from './models';

export interface ListQuery {
  companyId: string;
  direction?: Direction | null;
  /** One status, or any of several. */
  status?: ReceiptStatus | ReceiptStatus[] | null;
  period?: string | null;
  search?: string;
  page?: number;
  pageSize?: number;
}

/** Builds HttpParams from an object, skipping empty values; an array repeats its key. */
function params(values: Record<string, string | number | string[] | null | undefined>): HttpParams {
  let p = new HttpParams();
  for (const [k, v] of Object.entries(values)) {
    if (Array.isArray(v)) for (const item of v) p = p.append(k, item);
    else if (v != null && v !== '') p = p.set(k, v);
  }
  return p;
}

@Injectable({ providedIn: 'root' })
export class ReceiptApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  // ---------------- documents ----------------

  upload(companyId: string, direction: Direction, files: File[]): Observable<UploadResult[]> {
    const form = new FormData();
    for (const f of files) form.append('files', f, f.name);
    return this.http.post<UploadResult[]>(`${this.base}/receipts`, form, {
      params: params({ companyId, direction }),
    });
  }

  /** The blank form for a document typed in by hand (no file, no AI). Nothing is stored; its id is empty. */
  manualDraft(companyId: string, direction: Direction) {
    return this.http.post<ReceiptDetail>(`${this.base}/receipts/manual/draft`, { companyId, direction });
  }

  /** Validate a manual document that hasn't been saved yet. */
  validateManual(companyId: string, data: ReceiptEditable) {
    return this.http.post<{ issues: ValidationIssue[] }>(`${this.base}/receipts/manual/validate`, {
      companyId,
      direction: data.direction,
      data,
    });
  }

  /** Store a document typed in by hand. */
  createManual(companyId: string, data: ReceiptEditable) {
    return this.http.post<ReceiptDetail>(`${this.base}/receipts/manual`, {
      companyId,
      direction: data.direction,
      data,
    });
  }

  list(q: ListQuery) {
    return this.http.get<PagedResult<ReceiptSummary>>(`${this.base}/receipts`, {
      params: params({ ...q }),
    });
  }

  stats(companyId: string, direction?: Direction | null, period?: string | null) {
    return this.http.get<StatusCount[]>(`${this.base}/receipts/stats`, {
      params: params({ companyId, direction, period }),
    });
  }

  get(id: string) {
    return this.http.get<ReceiptDetail>(`${this.base}/receipts/${id}`);
  }

  update(id: string, body: ReceiptEditable) {
    return this.http.put<ReceiptDetail>(`${this.base}/receipts/${id}`, body);
  }

  validate(id: string, body: ReceiptEditable) {
    return this.http.post<{ issues: ValidationIssue[] }>(
      `${this.base}/receipts/${id}/validate`,
      body,
    );
  }

  approve(id: string, force = false) {
    return this.http.post<ReceiptDetail>(`${this.base}/receipts/${id}/approve`, { force });
  }

  unapprove(id: string) {
    return this.http.post<ReceiptDetail>(`${this.base}/receipts/${id}/unapprove`, {});
  }

  reprocess(id: string) {
    return this.http.post<void>(`${this.base}/receipts/${id}/reprocess`, {});
  }

  delete(id: string) {
    return this.http.delete<void>(`${this.base}/receipts/${id}`);
  }

  addPayment(
    id: string,
    body: { date: string; amount: number; accountId: string | null; note: string | null; whtAmount?: number },
  ) {
    return this.http.post<ReceiptDetail>(`${this.base}/receipts/${id}/payments`, body);
  }

  deletePayment(id: string, paymentId: string) {
    return this.http.delete<ReceiptDetail>(`${this.base}/receipts/${id}/payments/${paymentId}`);
  }

  // ---------------- companies ----------------

  companies() {
    return this.http.get<Company[]>(`${this.base}/companies`);
  }

  createCompany(body: CompanyInput) {
    return this.http.post<Company>(`${this.base}/companies`, body);
  }

  updateCompany(id: string, body: CompanyInput) {
    return this.http.put<Company>(`${this.base}/companies/${id}`, body);
  }

  /** Admins only: removes the company and everything booked under it. */
  deleteCompany(id: string) {
    return this.http.delete<void>(`${this.base}/companies/${id}`);
  }

  accounts(companyId: string) {
    return this.http.get<Account[]>(`${this.base}/companies/${companyId}/accounts`);
  }

  createAccount(companyId: string, body: AccountInput) {
    return this.http.post<Account>(`${this.base}/companies/${companyId}/accounts`, body);
  }

  updateAccount(companyId: string, accountId: string, body: AccountInput) {
    return this.http.put<Account>(
      `${this.base}/companies/${companyId}/accounts/${accountId}`,
      body,
    );
  }

  /** Admins only: removes an unused, non-system account (a used one can only be switched off). */
  deleteAccount(companyId: string, accountId: string) {
    return this.http.delete<void>(`${this.base}/companies/${companyId}/accounts/${accountId}`);
  }

  // ---------------- inventory ----------------

  productCategories(companyId: string) {
    return this.http.get<ProductCategory[]>(`${this.base}/companies/${companyId}/product-categories`);
  }

  createProductCategory(companyId: string, body: ProductCategoryInput) {
    return this.http.post<ProductCategory>(`${this.base}/companies/${companyId}/product-categories`, body);
  }

  updateProductCategory(companyId: string, categoryId: string, body: ProductCategoryInput) {
    return this.http.put<ProductCategory>(
      `${this.base}/companies/${companyId}/product-categories/${categoryId}`,
      body,
    );
  }

  /** Every category of the company, first to last. */
  reorderProductCategories(companyId: string, categoryIds: string[]) {
    return this.http.put<void>(`${this.base}/companies/${companyId}/product-categories/order`, { categoryIds });
  }

  /** Only an empty category can be removed; one with products is switched off instead. */
  deleteProductCategory(companyId: string, categoryId: string) {
    return this.http.delete<void>(`${this.base}/companies/${companyId}/product-categories/${categoryId}`);
  }

  products(companyId: string) {
    return this.http.get<Product[]>(`${this.base}/companies/${companyId}/products`);
  }

  /** The product a scanned label or typed code stands for (404 when there is none). */
  lookupProduct(companyId: string, code: string) {
    return this.http.get<Product>(`${this.base}/companies/${companyId}/products/lookup`, {
      params: params({ code }),
    });
  }

  /** With its stock per warehouse and ยอดยกมา. */
  product(companyId: string, productId: string) {
    return this.http.get<ProductDetail>(`${this.base}/companies/${companyId}/products/${productId}`);
  }

  createProduct(companyId: string, body: ProductInput) {
    return this.http.post<ProductDetail>(`${this.base}/companies/${companyId}/products`, body);
  }

  updateProduct(companyId: string, productId: string, body: ProductInput) {
    return this.http.put<ProductDetail>(`${this.base}/companies/${companyId}/products/${productId}`, body);
  }

  /** Only a product without stock history (other than ยอดยกมา) can be removed. */
  deleteProduct(companyId: string, productId: string) {
    return this.http.delete<void>(`${this.base}/companies/${companyId}/products/${productId}`);
  }

  /** The code pattern from the questionnaire, or null when it hasn't been answered (the AI suggestions are off). */
  codeScheme(companyId: string): Observable<CodeScheme | null> {
    return this.http.get<CodeScheme | null>(`${this.base}/companies/${companyId}/code-scheme`);
  }

  /** The AI's pattern from the questionnaire; nothing is saved. */
  proposeCodeScheme(companyId: string, body: SchemeQuestion) {
    return this.http.post<SchemeProposal>(`${this.base}/companies/${companyId}/code-scheme/propose`, body);
  }

  /** A first answer to "how do products differ", one block per product type. */
  suggestCodeNotes(companyId: string, body: SchemeQuestion) {
    return this.http.post<{ notes: string }>(`${this.base}/companies/${companyId}/code-scheme/suggest-notes`, body);
  }

  saveCodeScheme(companyId: string, body: CodeSchemeInput) {
    return this.http.put<CodeScheme>(`${this.base}/companies/${companyId}/code-scheme`, body);
  }

  /** Forgets the pattern and every learned code: the AI suggestions are off again. */
  deleteCodeScheme(companyId: string) {
    return this.http.delete<void>(`${this.base}/companies/${companyId}/code-scheme`);
  }

  deleteCodeTerm(companyId: string, termId: string) {
    return this.http.delete<void>(`${this.base}/companies/${companyId}/code-scheme/terms/${termId}`);
  }

  suggestCategoryCode(companyId: string, name: string, categoryId: string | null) {
    return this.http.post<CategoryCodeSuggestion>(`${this.base}/companies/${companyId}/code-scheme/suggest-category`, {
      name,
      categoryId,
    });
  }

  suggestProductCode(companyId: string, categoryId: string, name: string, productId: string | null) {
    return this.http.post<ProductCodeSuggestion>(`${this.base}/companies/${companyId}/code-scheme/suggest-product`, {
      categoryId,
      name,
      productId,
    });
  }

  warehouses(companyId: string) {
    return this.http.get<Warehouse[]>(`${this.base}/companies/${companyId}/warehouses`);
  }

  createWarehouse(companyId: string, body: WarehouseInput) {
    return this.http.post<Warehouse>(`${this.base}/companies/${companyId}/warehouses`, body);
  }

  updateWarehouse(companyId: string, warehouseId: string, body: WarehouseInput) {
    return this.http.put<Warehouse>(`${this.base}/companies/${companyId}/warehouses/${warehouseId}`, body);
  }

  /** The default warehouse can't be removed. */
  deleteWarehouse(companyId: string, warehouseId: string) {
    return this.http.delete<void>(`${this.base}/companies/${companyId}/warehouses/${warehouseId}`);
  }

  // ---------------- accounting ----------------

  periods(companyId: string) {
    return this.http.get<PeriodSummary[]>(`${this.base}/companies/${companyId}/periods`);
  }

  closePeriod(companyId: string, period: string) {
    return this.http.post<void>(`${this.base}/companies/${companyId}/periods/${period}/close`, {});
  }

  reopenPeriod(companyId: string, period: string) {
    return this.http.post<void>(`${this.base}/companies/${companyId}/periods/${period}/reopen`, {});
  }

  journal(companyId: string, period: string) {
    return this.http.get<Journal>(`${this.base}/companies/${companyId}/journal`, {
      params: params({ period }),
    });
  }

  journalCsvUrl(companyId: string, period: string) {
    return `${this.base}/companies/${companyId}/journal.csv?period=${encodeURIComponent(period)}`;
  }

  trialBalance(companyId: string, period: string) {
    return this.http.get<TrialBalance>(`${this.base}/companies/${companyId}/trial-balance`, {
      params: params({ period }),
    });
  }

  outstanding(companyId: string, direction: Direction) {
    return this.http.get<Outstanding>(`${this.base}/companies/${companyId}/outstanding`, {
      params: params({ direction }),
    });
  }

  // ---------------- AI learning ----------------

  accuracy(companyId?: string | null) {
    return this.http.get<Accuracy>(`${this.base}/ai/accuracy`, { params: params({ companyId }) });
  }

  issuers() {
    return this.http.get<Issuer[]>(`${this.base}/ai/issuers`);
  }

  exampleJson(id: string) {
    return this.http.get<unknown>(`${this.base}/ai/examples/${id}`);
  }

  setPinned(id: string, pinned: boolean) {
    return this.http.post<void>(`${this.base}/ai/examples/${id}/${pinned ? 'pin' : 'unpin'}`, {});
  }

  deleteExample(id: string) {
    return this.http.delete<void>(`${this.base}/ai/examples/${id}`);
  }

  /** Pin an approved document as a Training AI example of its issuer, or stop pinning it. */
  setReceiptPinned(receiptId: string, pinned: boolean) {
    const path = `${this.base}/ai/examples/from-receipt/${receiptId}`;
    return this.http.post<void>(pinned ? path : `${path}/unpin`, {});
  }

  datasetUrl(companyId?: string | null) {
    return `${this.base}/ai/dataset.zip${companyId ? `?companyId=${companyId}` : ''}`;
  }

  // ---------------- users (admins) ----------------

  users() {
    return this.http.get<User[]>(`${this.base}/users`);
  }

  createUser(body: UserInput) {
    return this.http.post<User>(`${this.base}/users`, body);
  }

  updateUser(id: string, body: UserUpdate) {
    return this.http.put<User>(`${this.base}/users/${id}`, body);
  }

  resetPassword(id: string, password: string) {
    return this.http.post<void>(`${this.base}/users/${id}/reset-password`, { password });
  }
}

/**
 * Loading a stock page failed. A bare 404 (no ProblemDetails) means the route itself is missing: the API is an
 * older build without the stock endpoints.
 */
export function stockLoadError(err: unknown): string {
  const e = err as { status?: number; error?: unknown };
  return e?.status === 404 && !e.error
    ? 'API ยังไม่มีระบบสต็อก — อัปเดต API เป็นเวอร์ชันล่าสุดแล้วโหลดหน้านี้ใหม่'
    : errorMessage(err);
}

/** Pulls a readable message out of an HttpErrorResponse (ProblemDetails or plain). */
export function errorMessage(err: unknown): string {
  const e = err as {
    error?: { detail?: string; title?: string; error?: string } | string;
    message?: string;
  };
  if (typeof e?.error === 'string' && e.error) return e.error;
  if (e?.error && typeof e.error === 'object')
    return e.error.detail ?? e.error.error ?? e.error.title ?? 'เกิดข้อผิดพลาด';
  return e?.message ?? 'เกิดข้อผิดพลาด';
}
