import { DatePipe, DecimalPipe, JsonPipe } from '@angular/common';
import { Component, DestroyRef, HostListener, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzDescriptionsModule } from 'ng-zorro-antd/descriptions';
import { NzDividerModule } from 'ng-zorro-antd/divider';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzGridModule } from 'ng-zorro-antd/grid';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule, NzModalService } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSpinModule } from 'ng-zorro-antd/spin';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { Subscription, debounceTime, filter, map, switchMap } from 'rxjs';
import { Auth } from '../../core/auth';
import { CompanyStore } from '../../core/company-store';
import { injectConfirm } from '../../core/confirm';
import { numberFormatter } from '../../core/number-format';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import {
  COUNTERPARTY_LABEL,
  DIRECTION_COLOR,
  DIRECTION_LABEL,
  DOC_TYPE_LABEL,
  Direction,
  DocumentType,
  LineItem,
  PAYMENT_STATUS_LABEL,
  ReceiptDetail,
  ReceiptEditable,
  ReceiptStatus,
  STATUS_COLOR,
  STATUS_LABEL,
  ValidationIssue,
  WHT_RATES,
  fromDate,
  fromMonth,
  periodLabel,
  toDate,
} from '../../core/models';

type LineGroup = FormGroup<{
  description: FormControl<string>;
  quantity: FormControl<number | null>;
  unitPrice: FormControl<number | null>;
  discount: FormControl<number | null>;
  amount: FormControl<number | null>;
}>;

const sameMonth = (a: Date, b: Date) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth();
const firstOfMonth = (d: Date) => new Date(d.getFullYear(), d.getMonth(), 1);
const money = (n: number) => n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

@Component({
  selector: 'app-review',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    DecimalPipe,
    JsonPipe,
    NzAlertModule,
    NzButtonModule,
    NzCardModule,
    NzCheckboxModule,
    NzDatePickerModule,
    NzDescriptionsModule,
    NzDividerModule,
    NzFormModule,
    NzGridModule,
    NzIconModule,
    NzInputModule,
    NzInputNumberModule,
    NzModalModule,
    NzSelectModule,
    NzSpinModule,
    NzTableModule,
    NzTagModule,
    NzTooltipModule,
  ],
  templateUrl: './review.html',
  styleUrl: './review.scss',
})
export class Review {
  /** Route param, bound by withComponentInputBinding(). "new" opens an unsaved manual document. */
  readonly id = input.required<string>();

  private readonly api = inject(ReceiptApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly message = inject(NzMessageService);
  private readonly modal = inject(NzModalService);
  private readonly confirm = injectConfirm();
  protected readonly store = inject(CompanyStore);
  /** What the signed-in role may do here (approve, payments, Training AI, debug). */
  protected readonly auth = inject(Auth);

  protected readonly statusLabel = STATUS_LABEL;
  protected readonly statusColor = STATUS_COLOR;
  protected readonly dirLabel = DIRECTION_LABEL;
  protected readonly dirColor = DIRECTION_COLOR;
  protected readonly counterpartyLabel = COUNTERPARTY_LABEL;
  protected readonly payLabel = PAYMENT_STATUS_LABEL;
  protected readonly periodLabel = periodLabel;
  protected readonly docTypes = Object.entries(DOC_TYPE_LABEL) as [DocumentType, string][];

  protected readonly receipt = signal<ReceiptDetail | null>(null);
  protected readonly liveIssues = signal<ValidationIssue[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly showDebug = signal(false);

  // image viewer
  protected readonly zoom = signal(1);
  protected readonly rotation = signal(0);
  protected readonly isPdf = computed(() => this.receipt()?.contentType === 'application/pdf');
  // Same-origin API URL; the browser's built-in PDF viewer handles paging and zoom.
  // Keyed on the URL string so saving the form doesn't reload the iframe.
  private readonly fileUrl = computed(() => this.receipt()?.fileUrl ?? null);
  protected readonly pdfUrl = computed(() => {
    const url = this.fileUrl();
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });

  protected readonly issues = computed(() => this.liveIssues() ?? this.receipt()?.issues ?? []);
  protected readonly errors = computed(() => this.issues().filter((i) => i.severity === 'Error'));
  protected readonly warnings = computed(() => this.issues().filter((i) => i.severity !== 'Error'));
  /** Errors first; the panel shows the first few and folds the rest behind "แสดงอีก N รายการ". */
  protected readonly sortedIssues = computed(() => [...this.errors(), ...this.warnings()]);
  protected readonly issuePreview = 3;
  protected readonly issuesExpanded = signal(false);
  protected readonly visibleIssues = computed(() =>
    this.issuesExpanded() ? this.sortedIssues() : this.sortedIssues().slice(0, this.issuePreview),
  );
  protected readonly isWaiting = computed(() => {
    const s = this.receipt()?.status;
    return s === 'Pending' || s === 'Processing';
  });
  protected readonly isApproved = computed(() => this.receipt()?.status === 'Approved');
  protected readonly isFailed = computed(() => this.receipt()?.status === 'Failed');
  /** A manual document that exists only in this form until it's saved. */
  protected readonly isDraft = computed(() => this.id() === 'new');
  /** By id, so reloading the company list doesn't reset a draft. */
  private readonly companyId = computed(() => this.store.current()?.id ?? null);

  protected readonly form = this.fb.group({
    direction: this.fb.nonNullable.control<Direction>('Purchase'),
    period: this.fb.control<Date | null>(null),
    documentType: this.fb.nonNullable.control<DocumentType>('Unknown'),
    documentNumber: this.fb.control<string | null>(null),
    issueDate: this.fb.control<Date | null>(null),
    dueDate: this.fb.control<Date | null>(null),
    referenceDocumentNumber: this.fb.control<string | null>(null),
    accountId: this.fb.control<string | null>(null),
    // The counterparty — the seller on purchases, the buyer on sales. Our side comes from the company profile.
    cpName: this.fb.control<string | null>(null),
    cpTaxId: this.fb.control<string | null>(null),
    cpBranch: this.fb.control<string | null>(null),
    cpAddress: this.fb.control<string | null>(null),
    subtotal: this.fb.control<number | null>(null),
    discount: this.fb.control<number | null>(null),
    vatAmount: this.fb.control<number | null>(null),
    total: this.fb.control<number | null>(null),
    vatIncluded: this.fb.nonNullable.control(false),
    whtRate: this.fb.control<number | null>(null),
    whtAmount: this.fb.control<number | null>(null),
    currency: this.fb.control<string | null>('THB'),
    paymentMethod: this.fb.control<string | null>(null),
    notes: this.fb.control<string | null>(null),
    lineItems: this.fb.array<LineGroup>([]),
  });

  protected readonly paymentForm = this.fb.group({
    date: this.fb.control<Date | null>(new Date()),
    amount: this.fb.control<number | null>(null),
    accountId: this.fb.control<string | null>(null),
    note: this.fb.control<string | null>(null),
    whtAmount: this.fb.control<number | null>(null),
  });

  get lines(): FormArray<LineGroup> {
    return this.form.controls.lineItems;
  }

  private readonly formValue = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });
  protected readonly direction = computed(() => this.formValue().direction);
  protected readonly docType = computed(() => this.formValue().documentType);
  protected readonly isNote = computed(() => this.docType() === 'CreditNote' || this.docType() === 'DebitNote');
  /** "seller" or "buyer": the side the counterparty inputs map to, for issue highlighting. */
  protected readonly cpSide = computed(() => (this.direction() === 'Purchase' ? 'seller' : 'buyer'));
  protected readonly accountOptions = computed(() =>
    this.direction() === 'Purchase' ? this.store.expenseAccounts() : this.store.revenueAccounts(),
  );
  protected readonly defaultAccountName = computed(() => {
    const role =
      this.direction() === 'Purchase' ? 'DefaultExpense' : this.docType() === 'CreditNote' ? 'SalesReturns' : 'SalesRevenue';
    return this.store.accounts().find((a) => a.role === role)?.name ?? '';
  });
  /** Our side as stored on the document (filled from the profile by the server). */
  protected readonly ourSide = computed(() => {
    const r = this.receipt();
    if (!r) return null;
    return r.direction === 'Purchase'
      ? { name: r.buyerName, taxId: r.buyerTaxId, branch: r.buyerBranch, address: r.buyerAddress }
      : { name: r.sellerName, taxId: r.sellerTaxId, branch: r.sellerBranch, address: r.sellerAddress };
  });

  /** Small arithmetic helpers shown next to the totals. */
  protected readonly lineSum = signal<number | null>(null);

  /** Thousands separators in the number inputs. */
  protected readonly money = numberFormatter(2);
  protected readonly qty = numberFormatter(null);

  protected readonly whtRates = WHT_RATES;
  /** Withholding tax last filled in from the rate; a hand-typed amount is left alone when the totals change. */
  private lastAutoWht: number | null = null;
  /** What actually changes hands: the total less withholding tax. */
  protected readonly netPayable = computed(() => {
    const v = this.formValue();
    return v.total != null && v.whtAmount ? Math.round((v.total - v.whtAmount) * 100) / 100 : null;
  });
  protected readonly vatHint = signal<number | null>(null);

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  /** Issue date before the latest edit, to tell an untouched period from one moved on purpose. */
  private lastIssueDate: Date | null = null;
  private loadSub: Subscription | null = null;

  constructor() {
    effect(() => {
      const id = this.id();
      if (id !== 'new') {
        this.load(id);
        return;
      }
      const companyId = this.companyId();
      if (companyId) this.loadDraft(companyId);
    });

    // Live validation while the reviewer edits.
    this.form.valueChanges
      .pipe(
        debounceTime(400),
        filter(() => this.form.dirty && !this.isApproved() && !!this.receipt()),
        switchMap(() =>
          this.isDraft()
            ? this.api.validateManual(this.receipt()!.companyId, this.payload())
            : this.api.validate(this.receipt()!.id, this.payload()),
        ),
        takeUntilDestroyed(),
      )
      .subscribe({ next: (r) => this.liveIssues.set(r.issues) });

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.updateHints());

    // Withholding tax follows the rate × the amount before VAT, unless the amount was typed by hand.
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.fillWht());

    // The period follows the document date unless the reviewer moved it on purpose.
    this.form.controls.issueDate.valueChanges.pipe(takeUntilDestroyed()).subscribe((next) => {
      const prev = this.lastIssueDate;
      this.lastIssueDate = next;
      const period = this.form.controls.period;
      if (!next) return;
      if (!period.value || (prev && sameMonth(period.value, prev))) period.setValue(firstOfMonth(next));
    });

    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  // ---------------- data ----------------

  private load(id: string, silent = false) {
    if (!silent) {
      this.loadError.set(null);
      this.zoom.set(1);
      this.rotation.set(0);
    }
    this.loadSub?.unsubscribe();
    this.loadSub = this.api.get(id).subscribe({
      next: (r) => {
        this.show(r);
        if (r.status === 'Pending' || r.status === 'Processing') this.startPolling(id);
        else this.stopPolling();
      },
      error: (e) => this.loadError.set(errorMessage(e)),
    });
  }

  private loadDraft(companyId: string) {
    this.stopPolling();
    this.loadError.set(null);
    const direction: Direction = this.route.snapshot.queryParamMap.get('direction') === 'Sale' ? 'Sale' : 'Purchase';
    this.loadSub?.unsubscribe();
    this.loadSub = this.api.manualDraft(companyId, direction).subscribe({
      next: (r) => this.show(r),
      error: (e) => this.loadError.set(errorMessage(e)),
    });
  }

  private show(r: ReceiptDetail) {
    // Opening a document of another company switches the app to that company (accounts, menus).
    if (this.store.current()?.id !== r.companyId) this.store.select(r.companyId);
    this.receipt.set(r);
    this.liveIssues.set(null);
    this.patchForm(r);
    this.resetPaymentForm(r);
  }

  private startPolling(id: string) {
    if (this.pollTimer) return;
    this.pollTimer = setInterval(() => this.load(id, true), 2500);
  }

  private stopPolling() {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = null;
  }

  private patchForm(r: ReceiptDetail) {
    const purchase = r.direction === 'Purchase';
    this.lines.clear({ emitEvent: false });
    for (const li of r.lineItems) this.lines.push(this.lineGroup(li), { emitEvent: false });
    this.form.patchValue(
      {
        direction: r.direction,
        period: toDate(r.period),
        documentType: r.documentType,
        documentNumber: r.documentNumber,
        issueDate: toDate(r.issueDate),
        dueDate: toDate(r.dueDate),
        referenceDocumentNumber: r.referenceDocumentNumber,
        accountId: r.accountId,
        cpName: purchase ? r.sellerName : r.buyerName,
        cpTaxId: purchase ? r.sellerTaxId : r.buyerTaxId,
        cpBranch: purchase ? r.sellerBranch : r.buyerBranch,
        cpAddress: purchase ? r.sellerAddress : r.buyerAddress,
        subtotal: r.subtotal,
        discount: r.discount,
        vatAmount: r.vatAmount,
        total: r.total,
        vatIncluded: r.vatIncluded,
        currency: r.currency,
        paymentMethod: r.paymentMethod,
        notes: r.notes,
        whtRate: r.whtRate,
        whtAmount: r.whtAmount,
      },
      { emitEvent: false },
    );
    // A saved amount counts as filled from the rate only if it is exactly what the rate gives.
    const auto = r.whtRate != null && r.total != null ? Math.round((r.total - (r.vatAmount ?? 0)) * r.whtRate) / 100 : null;
    this.lastAutoWht = auto != null && auto === r.whtAmount ? auto : null;
    this.lastIssueDate = this.form.controls.issueDate.value;
    if (r.status === 'Approved' || r.status === 'Pending' || r.status === 'Processing') this.form.disable({ emitEvent: false });
    else this.form.enable({ emitEvent: false });
    // Refresh the computed view of the form (direction, type) without triggering validation.
    this.form.updateValueAndValidity({ emitEvent: true });
    this.form.markAsPristine();
    this.updateHints();
  }

  private resetPaymentForm(r: ReceiptDetail) {
    const bank = this.store.accounts().find((a) => a.role === 'Bank');
    // The money part is what's left after the withholding tax still to settle.
    const cash = Math.round((r.outstanding - r.whtRemaining) * 100) / 100;
    this.paymentForm.reset({
      date: new Date(),
      amount: cash > 0 ? cash : null,
      accountId: bank?.id ?? null,
      note: null,
      whtAmount: r.whtRemaining > 0 ? r.whtRemaining : null,
    });
  }

  private lineGroup(li?: Partial<LineItem>): LineGroup {
    return this.fb.group({
      description: this.fb.nonNullable.control(li?.description ?? ''),
      quantity: this.fb.control<number | null>(li?.quantity ?? null),
      unitPrice: this.fb.control<number | null>(li?.unitPrice ?? null),
      discount: this.fb.control<number | null>(li?.discount ?? null),
      amount: this.fb.control<number | null>(li?.amount ?? null),
    });
  }

  private payload(): ReceiptEditable {
    const v = this.form.getRawValue();
    const s = (x: string | null) => (x == null || x.trim() === '' ? null : x.trim());
    const purchase = v.direction === 'Purchase';
    const cp = { name: s(v.cpName), taxId: s(v.cpTaxId), branch: s(v.cpBranch), address: s(v.cpAddress) };
    return {
      direction: v.direction,
      period: fromMonth(v.period),
      documentType: v.documentType,
      documentNumber: s(v.documentNumber),
      issueDate: fromDate(v.issueDate),
      dueDate: fromDate(v.dueDate),
      referenceDocumentNumber: s(v.referenceDocumentNumber),
      // Our side is filled in by the server from the company profile.
      sellerName: purchase ? cp.name : null,
      sellerTaxId: purchase ? cp.taxId : null,
      sellerBranch: purchase ? cp.branch : null,
      sellerAddress: purchase ? cp.address : null,
      buyerName: purchase ? null : cp.name,
      buyerTaxId: purchase ? null : cp.taxId,
      buyerBranch: purchase ? null : cp.branch,
      buyerAddress: purchase ? null : cp.address,
      subtotal: v.subtotal,
      discount: v.discount,
      vatAmount: v.vatAmount,
      total: v.total,
      vatIncluded: v.vatIncluded,
      currency: s(v.currency),
      paymentMethod: s(v.paymentMethod),
      accountId: v.accountId || null,
      notes: s(v.notes),
      whtRate: v.whtAmount ? v.whtRate : null,
      whtAmount: v.whtAmount || null,
      lineItems: v.lineItems.map((li, i) => ({
        lineNo: i + 1,
        description: li.description,
        quantity: li.quantity,
        unitPrice: li.unitPrice,
        discount: li.discount,
        amount: li.amount,
      })),
    };
  }

  private fillWht() {
    const { whtRate, whtAmount, total, vatAmount } = this.form.getRawValue();
    const ctrl = this.form.controls.whtAmount;
    if (this.form.disabled) return;
    if (whtRate == null) {
      if (whtAmount != null && whtAmount === this.lastAutoWht) ctrl.setValue(null, { emitEvent: false });
      this.lastAutoWht = null;
      return;
    }
    if (total == null) return;
    const next = Math.round((total - (vatAmount ?? 0)) * whtRate) / 100;
    if (whtAmount == null || whtAmount === this.lastAutoWht) {
      if (whtAmount !== next) ctrl.setValue(next, { emitEvent: false });
      this.lastAutoWht = next;
    }
  }

  private updateHints() {
    const v = this.form.getRawValue();
    const amounts = v.lineItems.map((l) => l.amount);
    this.lineSum.set(amounts.length && amounts.every((a) => a != null) ? amounts.reduce((a, b) => a! + b!, 0)! : null);
    this.vatHint.set(v.total != null ? Math.round(((v.total * 7) / 107) * 100) / 100 : null);
  }

  // ---------------- issues ----------------

  /** The most severe issue for a field path, e.g. "total" or "lineItems.0.amount". */
  protected issueFor(field: string): ValidationIssue | undefined {
    const list = this.issues().filter((i) => i.field === field);
    return list.find((i) => i.severity === 'Error') ?? list[0];
  }

  /** nz-form-control validate status for a field: red on errors, amber on warnings. */
  protected statusOf(field: string): 'error' | 'warning' | '' {
    const i = this.issueFor(field);
    return i ? (i.severity === 'Error' ? 'error' : 'warning') : '';
  }

  protected focusField(field: string | null) {
    if (!field) return;
    const host = document.querySelector<HTMLElement>(`[data-field="${field}"]`);
    // nz components carry the data-field on their host; focus the input inside.
    const el = host?.matches('input, textarea') ? host : host?.querySelector<HTMLElement>('input, textarea') ?? host;
    host?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    setTimeout(() => el?.focus(), 250);
  }

  // ---------------- line items ----------------

  protected addLine() {
    this.lines.push(this.lineGroup());
    this.form.markAsDirty();
  }

  protected removeLine(i: number) {
    this.lines.removeAt(i);
    this.form.markAsDirty();
  }

  /** Fill amount = qty × unit price − discount when amount is empty. */
  protected fillAmount(i: number) {
    const g = this.lines.at(i);
    const { quantity, unitPrice, discount, amount } = g.getRawValue();
    if (amount == null && quantity != null && unitPrice != null) {
      g.controls.amount.setValue(Math.round((quantity * unitPrice - (discount ?? 0)) * 100) / 100);
      this.form.markAsDirty();
    }
  }

  protected useLineSumAsSubtotal() {
    if (this.lineSum() != null) {
      this.form.controls.subtotal.setValue(this.lineSum());
      this.form.markAsDirty();
    }
  }

  // ---------------- actions ----------------

  protected save(then?: (r: ReceiptDetail) => void) {
    const r = this.receipt();
    if (!r || this.busy()) return;
    this.busy.set(true);
    const draft = this.isDraft();
    const request = draft ? this.api.createManual(r.companyId, this.payload()) : this.api.update(r.id, this.payload());
    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.show(saved);
        // The document exists now: give it its real address (back no longer reopens a blank form).
        if (draft) this.router.navigate(['/receipts', saved.id], { replaceUrl: true });
        if (then) then(saved);
        else this.message.success('บันทึกแล้ว');
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected approve() {
    if (!this.receipt() || !this.auth.canApprove()) return;
    const send = (id: string, force: boolean) => {
      this.busy.set(true);
      this.api.approve(id, force).subscribe({
        // Stay on the approved document; the reviewer moves on with "ใบถัดไป" when ready.
        next: (approved) => {
          this.busy.set(false);
          this.show(approved);
          this.message.success('อนุมัติแล้ว');
        },
        error: (e) => {
          this.busy.set(false);
          this.message.error(errorMessage(e));
        },
      });
    };
    const doApprove = (id: string) => {
      if (this.errors().length === 0) {
        send(id, false);
        return;
      }
      this.modal.confirm({
        nzTitle: `ยังมี Error ${this.errors().length} รายการ`,
        nzContent: 'ต้องการอนุมัติต่อหรือไม่? ตัวเลขที่ผิดกฎอาจทำให้บัญชีไม่ถูกต้อง',
        nzOkText: 'อนุมัติต่อ',
        nzOkDanger: true,
        nzCancelText: 'กลับไปแก้',
        nzOnOk: () => send(id, true),
      });
    };
    // A draft has to be stored before it can be approved.
    if (this.form.dirty || this.isDraft()) this.save((saved) => doApprove(saved.id));
    else doApprove(this.receipt()!.id);
  }

  protected unapprove() {
    const r = this.receipt();
    if (!r) return;
    this.busy.set(true);
    this.api.unapprove(r.id).subscribe({
      next: (u) => {
        this.busy.set(false);
        this.show(u);
        this.message.info('ยกเลิกอนุมัติแล้ว แก้ไขได้');
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected askReprocess() {
    if (!this.form.dirty) {
      this.reprocess();
      return;
    }
    this.confirm({
      title: 'อ่านเอกสารใหม่?',
      content: 'การแก้ไขที่ยังไม่บันทึกจะหายไป',
      okText: 'อ่านใหม่',
      onOk: () => this.reprocess(),
    });
  }

  protected reprocess() {
    const r = this.receipt();
    if (!r) return;
    this.api.reprocess(r.id).subscribe({
      next: () => {
        this.message.info('ส่งเข้าคิวอ่านใหม่แล้ว');
        this.load(r.id, true);
      },
      error: (e) => this.message.error(errorMessage(e)),
    });
  }

  /** Leave an unsaved manual document; nothing was stored, so there is nothing to delete. */
  protected discardDraft() {
    const leave = () => this.router.navigate(['/receipts'], { queryParams: { direction: this.direction() } });
    if (!this.form.dirty) {
      leave();
      return;
    }
    this.confirm({ title: 'ยกเลิกเอกสารนี้?', content: 'ข้อมูลที่กรอกจะหายไป', okText: 'ทิ้งข้อมูล', danger: true, onOk: leave });
  }

  protected askRemove() {
    const r = this.receipt();
    if (!r) return;
    this.confirm({
      title: `ลบ “${r.originalFileName}” ?`,
      content: 'ลบแล้วกู้คืนไม่ได้',
      okText: 'ลบ',
      danger: true,
      onOk: () => this.remove(),
    });
  }

  protected remove() {
    const r = this.receipt();
    if (!r) return;
    this.api.delete(r.id).subscribe({
      next: () => this.router.navigate(['/receipts'], { queryParams: { direction: r.direction } }),
      error: (e) => this.message.error(errorMessage(e)),
    });
  }

  /** Pin or unpin this approved document as a Training AI example of its issuer. */
  protected toggleExample() {
    const r = this.receipt();
    if (!r || this.busy()) return;
    const pinned = !r.examplePinned;
    this.busy.set(true);
    this.api.setReceiptPinned(r.id, pinned).subscribe({
      next: () => {
        this.busy.set(false);
        this.receipt.set({ ...r, examplePinned: pinned });
        this.message.success(pinned ? 'ปักหมุดเป็นตัวอย่าง Training AI แล้ว' : 'เลิกปักหมุดตัวอย่างแล้ว');
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  // ---------------- payments ----------------

  protected addPayment() {
    const r = this.receipt();
    const v = this.paymentForm.getRawValue();
    const date = fromDate(v.date);
    const amount = v.amount ?? 0;
    const whtAmount = v.whtAmount ?? 0;
    if (!r || amount < 0 || whtAmount < 0 || amount + whtAmount <= 0 || !date) {
      this.message.warning('กรอกวันที่และจำนวนเงิน');
      return;
    }
    this.busy.set(true);
    this.api.addPayment(r.id, { date, amount, accountId: v.accountId, note: v.note, whtAmount }).subscribe({
      next: (u) => {
        this.busy.set(false);
        this.receipt.set(u);
        this.resetPaymentForm(u);
        this.message.success(r.direction === 'Purchase' ? 'บันทึกการจ่ายเงินแล้ว' : 'บันทึกการรับเงินแล้ว');
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected askDeletePayment(p: { id: string; amount: number }) {
    this.confirm({
      title: 'ลบรายการชำระนี้?',
      content: `จำนวน ${money(p.amount)} บาท`,
      okText: 'ลบ',
      danger: true,
      onOk: () => this.deletePayment(p.id),
    });
  }

  protected deletePayment(paymentId: string) {
    const r = this.receipt();
    if (!r) return;
    this.api.deletePayment(r.id, paymentId).subscribe({
      next: (u) => {
        this.receipt.set(u);
        this.resetPaymentForm(u);
      },
      error: (e) => this.message.error(errorMessage(e)),
    });
  }

  /** Jump to the next document of the same company and side that still needs a human. */
  protected goNext() {
    const r = this.receipt();
    if (!r) return;
    const q = { companyId: r.companyId, direction: r.direction, pageSize: 5 };
    // Issues first, then ready to approve, then failed reads: the same set as the "รอจัดการ" tab.
    const order: ReceiptStatus[] = ['NeedsReview', 'Validated', 'Failed'];
    const tryNext = (i: number) => {
      if (i === order.length) {
        this.message.success('จัดการครบทุกใบแล้ว 🎉');
        this.router.navigate(['/receipts'], { queryParams: { direction: r.direction } });
        return;
      }
      this.api.list({ ...q, status: order[i] }).subscribe((res) => {
        const next = res.items.find((x) => x.id !== r.id);
        if (next) this.router.navigate(['/receipts', next.id]);
        else tryNext(i + 1);
      });
    };
    tryNext(0);
  }

  @HostListener('window:keydown', ['$event'])
  protected onKey(e: KeyboardEvent) {
    const mod = e.ctrlKey || e.metaKey;
    if (mod && e.key.toLowerCase() === 's') {
      e.preventDefault();
      if (!this.isApproved() && !this.isWaiting()) this.save();
    } else if (mod && e.key === 'Enter') {
      e.preventDefault();
      if (this.isApproved()) this.goNext();
      else if (!this.isWaiting() && this.auth.canApprove()) this.approve();
    }
  }

  // ---------------- viewer ----------------

  protected zoomBy(f: number) {
    this.zoom.update((z) => Math.min(5, Math.max(0.25, Math.round(z * f * 100) / 100)));
  }

  protected rotate() {
    this.rotation.update((r) => (r + 90) % 360);
  }

  protected onWheel(e: WheelEvent) {
    if (!e.ctrlKey) return;
    e.preventDefault();
    this.zoomBy(e.deltaY < 0 ? 1.1 : 1 / 1.1);
  }

  protected prettyJson(raw: string | null): unknown {
    if (!raw) return null;
    try {
      return JSON.parse(raw);
    } catch {
      return raw;
    }
  }
}
