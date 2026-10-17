import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzResultModule } from 'ng-zorro-antd/result';
import { NzPaginationModule } from 'ng-zorro-antd/pagination';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSpinModule } from 'ng-zorro-antd/spin';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzUploadFile, NzUploadModule } from 'ng-zorro-antd/upload';
import { Subject, debounceTime } from 'rxjs';
import { CompanyStore } from '../../core/company-store';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import {
  COUNTERPARTY_LABEL,
  DIRECTION_LABEL,
  DOC_TYPE_LABEL,
  Direction,
  PAYMENT_STATUS_LABEL,
  PagedResult,
  ReceiptStatus,
  ReceiptSummary,
  STATUS_COLOR,
  STATUS_LABEL,
  UploadResult,
  currentPeriod,
  periodLabel,
} from '../../core/models';

/**
 * "ToReview" (รอจัดการ) is the daily work: everything that needs a person, whether to check and approve
 * (with or without issues, each row still shows which) or to deal with a failed read.
 */
type StatusFilter = 'ToReview' | 'All' | 'Pending' | 'Processing' | 'Approved' | 'Failed';

const STATUS_TABS: StatusFilter[] = ['All', 'ToReview', 'Pending', 'Processing', 'Approved', 'Failed'];

/** The document statuses behind each tab; "All" sends none. */
const TAB_STATUSES: Record<StatusFilter, ReceiptStatus[]> = {
  ToReview: ['NeedsReview', 'Validated', 'Failed'],
  All: [],
  Pending: ['Pending'],
  Processing: ['Processing'],
  Approved: ['Approved'],
  Failed: ['Failed'],
};

const TAB_LABEL: Record<StatusFilter, string> = {
  ToReview: 'รอจัดการ',
  All: 'ทั้งหมด',
  Pending: STATUS_LABEL.Pending,
  Processing: STATUS_LABEL.Processing,
  Approved: STATUS_LABEL.Approved,
  Failed: STATUS_LABEL.Failed,
};

/** Count-badge colour per status chip; only applied while the count is non-zero. */
const STATUS_TONE: Record<StatusFilter, 'neutral' | 'warn' | 'error' | 'info' | 'ok'> = {
  ToReview: 'warn',
  All: 'neutral',
  Processing: 'info',
  Pending: 'info',
  Approved: 'ok',
  Failed: 'error',
};

/** URL value of the period filter for "every period" (no param means the current month). */
const ALL_PERIODS = 'all';

@Component({
  selector: 'app-inbox',
  imports: [
    DatePipe,
    DecimalPipe,
    NgTemplateOutlet,
    RouterLink,
    FormsModule,
    NzAlertModule,
    NzButtonModule,
    NzCardModule,
    NzEmptyModule,
    NzIconModule,
    NzInputModule,
    NzResultModule,
    NzPaginationModule,
    NzSelectModule,
    NzSpinModule,
    NzTableModule,
    NzTagModule,
    NzUploadModule,
  ],
  templateUrl: './inbox.html',
  styleUrl: './inbox.scss',
})
export class Inbox {
  /** Optional deep-link query params, e.g. from the periods page: ?direction=Sale&period=2026-10 */
  readonly directionParam = input<string | undefined>(undefined, { alias: 'direction' });
  readonly periodParam = input<string | undefined>(undefined, { alias: 'period' });

  private readonly api = inject(ReceiptApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly message = inject(NzMessageService);
  protected readonly store = inject(CompanyStore);

  protected readonly statusLabel = STATUS_LABEL;
  protected readonly statusColor = STATUS_COLOR;
  protected readonly docTypeLabel = DOC_TYPE_LABEL;
  protected readonly dirLabel = DIRECTION_LABEL;
  protected readonly counterpartyLabel = COUNTERPARTY_LABEL;
  protected readonly payLabel = PAYMENT_STATUS_LABEL;
  protected readonly periodLabel = periodLabel;

  protected readonly directionTabs: { value: Direction; label: string; icon: string }[] = [
    { value: 'Purchase', label: DIRECTION_LABEL.Purchase, icon: 'shopping-cart' },
    { value: 'Sale', label: DIRECTION_LABEL.Sale, icon: 'shop' },
  ];

  protected readonly direction = signal<Direction>('Purchase');
  /** null = every period. Opens on the current month. */
  protected readonly period = signal<string | null>(currentPeriod());
  protected readonly periods = signal<string[]>([]);
  /** Months with documents, plus this month and the selected one, so the picker never shows a blank value. */
  protected readonly periodOptions = computed(() => {
    const set = new Set([...this.periods(), currentPeriod()]);
    const selected = this.period();
    if (selected) set.add(selected);
    return [...set].sort().reverse();
  });
  protected readonly status = signal<StatusFilter>('All');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = 50;

  protected readonly result = signal<PagedResult<ReceiptSummary> | null>(null);
  protected readonly counts = signal<Partial<Record<ReceiptStatus, number>>>({});
  protected readonly loading = signal(false);
  protected readonly uploading = signal(false);
  protected readonly uploadResults = signal<UploadResult[]>([]);

  protected readonly totalCount = computed(() =>
    Object.values(this.counts()).reduce((a, b) => a + (b ?? 0), 0),
  );
  protected readonly inFlight = computed(() => (this.counts().Pending ?? 0) + (this.counts().Processing ?? 0));
  protected readonly statusTabs = computed(() =>
    STATUS_TABS.map((s) => ({
      value: s,
      label: TAB_LABEL[s],
      count: this.tabCount(s),
      tone: STATUS_TONE[s],
    })),
  );

  private readonly search$ = new Subject<string>();

  constructor() {
    effect(() => {
      const d = this.directionParam();
      const p = this.periodParam();
      untracked(() => {
        if (d === 'Purchase' || d === 'Sale') this.direction.set(d);
        // No param = the default (this month); "all" = every period, kept in the URL so a refresh doesn't reset it.
        this.period.set(p === undefined ? currentPeriod() : p === ALL_PERIODS || p === '' ? null : p);
      });
    });

    // Reload whenever the company changes (and on first load).
    effect(() => {
      const company = this.store.current();
      if (!company) return;
      untracked(() => {
        this.page.set(1);
        this.load();
        this.api.periods(company.id).subscribe((p) => this.periods.set(p.map((x) => x.period)));
      });
    });

    this.search$.pipe(debounceTime(300), takeUntilDestroyed()).subscribe((s) => {
      this.search.set(s);
      this.page.set(1);
      this.load();
    });

    // Poll while the worker still has documents in the queue.
    const timer = setInterval(() => {
      if (this.inFlight() > 0) this.load(true);
    }, 3000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  /** Until someone picks a tab, the page opens on what still needs doing, or on everything once that's empty. */
  private statusPicked = false;

  private tabCount(s: StatusFilter): number {
    return s === 'All' ? this.totalCount() : TAB_STATUSES[s].reduce((n, x) => n + (this.counts()[x] ?? 0), 0);
  }

  protected load(silent = false) {
    const company = this.store.current();
    if (!company) return;
    if (!silent) this.loading.set(true);
    const stats$ = this.api.stats(company.id, this.direction(), this.period());
    // Background refreshes never switch the tab under the reader.
    if (silent || this.statusPicked) {
      this.loadList(company.id);
      stats$.subscribe((s) => this.setCounts(s));
      return;
    }
    stats$.subscribe({
      next: (s) => {
        this.setCounts(s);
        this.status.set(this.tabCount('ToReview') > 0 ? 'ToReview' : 'All');
        this.loadList(company.id);
      },
      error: () => this.loadList(company.id),
    });
  }

  private setCounts(stats: { status: ReceiptStatus; count: number }[]) {
    const map: Partial<Record<ReceiptStatus, number>> = {};
    for (const c of stats) map[c.status] = c.count;
    this.counts.set(map);
  }

  private loadList(companyId: string) {
    const status = this.status();
    this.api
      .list({
        companyId,
        direction: this.direction(),
        period: this.period(),
        status: TAB_STATUSES[status],
        search: this.search(),
        page: this.page(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.loading.set(false);
        },
        error: (e) => {
          this.loading.set(false);
          this.message.error(errorMessage(e));
        },
      });
  }

  protected selectDirection(d: Direction) {
    this.direction.set(d);
    this.page.set(1);
    this.uploadResults.set([]);
    this.syncUrl();
    this.load();
  }

  protected selectPeriod(p: string | null) {
    this.period.set(p || null);
    this.page.set(1);
    this.syncUrl();
    this.load();
  }

  protected selectStatus(s: string | number) {
    this.statusPicked = true;
    this.status.set(s as StatusFilter);
    this.page.set(1);
    this.load();
  }

  protected onSearch(value: string) {
    this.search$.next(value.trim());
  }

  protected goPage(page: number) {
    this.page.set(page);
    this.load();
  }

  protected open(r: ReceiptSummary) {
    this.router.navigate(['/receipts', r.id]);
  }

  /** Types the row passed into the shared table/card ng-templates (template context is untyped). */
  protected asRow(r: unknown) {
    return r as ReceiptSummary;
  }

  protected openId(id: string | null) {
    if (id) this.router.navigate(['/receipts', id]);
  }

  /** Open a blank form of the current side; nothing is stored until it's saved. */
  protected createManual() {
    if (!this.store.current()) return;
    this.router.navigate(['/receipts', 'new'], { queryParams: { direction: this.direction() } });
  }

  private syncUrl() {
    this.router.navigate([], {
      queryParams: { direction: this.direction(), period: this.period() ?? ALL_PERIODS },
      replaceUrl: true,
    });
  }

  // ---------- upload ----------

  /**
   * nz-upload calls this once per file with the whole selection; upload the batch on the last call and
   * return false so nz-upload doesn't send anything itself.
   */
  protected readonly beforeUpload = (file: NzUploadFile, fileList: NzUploadFile[]): boolean => {
    if (file === fileList[fileList.length - 1]) this.upload(fileList as unknown as File[]);
    return false;
  };

  private upload(files: File[]) {
    const company = this.store.current();
    if (files.length === 0 || !company) return;
    this.uploading.set(true);
    this.api.upload(company.id, this.direction(), files).subscribe({
      next: (res) => {
        this.uploading.set(false);
        this.uploadResults.set(res);
        const ok = res.filter((r) => !r.error && !r.duplicate).length;
        const dup = res.filter((r) => r.duplicate).length;
        const bad = res.filter((r) => r.error).length;
        this.message.success(
          [`อัปโหลด ${ok} ไฟล์`, dup ? `ซ้ำ ${dup}` : '', bad ? `ไม่รองรับ ${bad}` : ''].filter(Boolean).join(' · '),
        );
        this.load(true);
      },
      error: (e) => {
        this.uploading.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }
}
