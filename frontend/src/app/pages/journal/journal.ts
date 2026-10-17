import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyStore } from '../../core/company-store';
import { ReceiptApi } from '../../core/receipt-api';
import {
  ACCOUNT_TYPE_LABEL,
  Journal,
  TrialBalance,
  currentPeriod,
  fromMonth,
  periodLabel,
  toDate,
} from '../../core/models';

@Component({
  selector: 'app-journal',
  imports: [
    DecimalPipe,
    DatePipe,
    RouterLink,
    FormsModule,
    NzAlertModule,
    NzButtonModule,
    NzCardModule,
    NzDatePickerModule,
    NzIconModule,
    NzTableModule,
    NzTagModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">สมุดรายวัน · {{ label(period()) }}</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            สร้างอัตโนมัติจากเอกสารที่อนุมัติแล้วและการชำระเงิน: ซื้อ = Dr ค่าใช้จ่าย + ภาษีซื้อ / Cr เจ้าหนี้ ·
            จ่ายเงิน = Dr เจ้าหนี้ / Cr เงินสด-ธนาคาร · ขาย = Dr ลูกหนี้ / Cr รายได้ + ภาษีขาย · ใบลดหนี้กลับด้าน
          </p>
        </div>
        <!-- phones: the view toggle takes the first row (two equal halves), month + CSV share the second -->
        <div class="flex flex-wrap items-center gap-2 max-sm:w-full">
          <nz-date-picker
            class="max-sm:flex-1"
            nzMode="month"
            nzFormat="MM/yyyy"
            [nzAllowClear]="false"
            [ngModel]="periodDate()"
            (ngModelChange)="setPeriod($event)"
          />
          <div class="seg seg-sm max-sm:order-first max-sm:w-full" role="tablist" aria-label="มุมมอง">
            @for (v of views; track v.value) {
              <button
                type="button"
                role="tab"
                class="seg-btn"
                [class.active]="view() === v.value"
                [attr.aria-selected]="view() === v.value"
                (click)="view.set(v.value)"
              >
                {{ v.label }}
              </button>
            }
          </div>
          @if (csvUrl(); as url) {
            <a nz-button [href]="url" download><nz-icon nzType="download" /> CSV</a>
          }
        </div>
      </header>

      @if (view() === 'journal') {
        @if (journal(); as j) {
          <nz-card [nzBodyStyle]="{ padding: '0' }">
            <nz-table nzSize="middle" [nzData]="j.entries" [nzFrontPagination]="false" [nzShowPagination]="false" [nzNoResult]="noEntries">
              <thead>
                <tr>
                  <th nzWidth="120px">วันที่</th>
                  <th>รายการ / บัญชี</th>
                  <th nzAlign="right" nzWidth="150px">เดบิต</th>
                  <th nzAlign="right" nzWidth="150px">เครดิต</th>
                </tr>
              </thead>
              <tbody>
                @for (e of j.entries; track $index) {
                  <tr class="entry">
                    <td class="num">{{ e.date | date: 'dd/MM/yyyy' }}</td>
                    <td colspan="3">
                      <a [routerLink]="['/receipts', e.receiptId]">{{ e.description }}</a>
                      <nz-tag class="kind" [nzColor]="e.kind === 'payment' ? 'blue' : 'default'">
                        {{ e.kind === 'payment' ? 'ชำระเงิน' : 'เอกสาร' }}
                      </nz-tag>
                    </td>
                  </tr>
                  @for (l of e.lines; track $index) {
                    <tr class="line">
                      <td></td>
                      <td [class.indent]="l.credit > 0"><span class="num muted">{{ l.accountCode }}</span> {{ l.accountName }}</td>
                      <td class="num" nzAlign="right">{{ l.debit ? (l.debit | number: '1.2-2') : '' }}</td>
                      <td class="num" nzAlign="right">{{ l.credit ? (l.credit | number: '1.2-2') : '' }}</td>
                    </tr>
                  }
                }
                @if (j.entries.length) {
                  <tr class="total">
                    <td></td>
                    <td>
                      รวม {{ j.entries.length }} รายการ
                      @if (j.totalDebit === j.totalCredit) {
                        <nz-tag nzColor="success">สมดุล</nz-tag>
                      } @else {
                        <nz-tag nzColor="error">ไม่สมดุล</nz-tag>
                      }
                    </td>
                    <td class="num" nzAlign="right">{{ j.totalDebit | number: '1.2-2' }}</td>
                    <td class="num" nzAlign="right">{{ j.totalCredit | number: '1.2-2' }}</td>
                  </tr>
                }
              </tbody>
            </nz-table>
            <ng-template #noEntries>ไม่มีรายการในงวดนี้ (นับเฉพาะเอกสารที่อนุมัติแล้ว)</ng-template>
          </nz-card>
        }
      } @else if (trial(); as t) {
        <nz-card [nzBodyStyle]="{ padding: '0' }">
          <nz-table #tb nzSize="middle" [nzData]="t.rows" [nzFrontPagination]="false" [nzShowPagination]="false">
            <thead>
              <tr>
                <th>รหัส</th>
                <th>บัญชี</th>
                <th class="max-md:hidden">หมวด</th>
                <th nzAlign="right">เดบิต (งวดนี้)</th>
                <th nzAlign="right">เครดิต (งวดนี้)</th>
                <th nzAlign="right">ยอดยกไป Dr</th>
                <th nzAlign="right">ยอดยกไป Cr</th>
              </tr>
            </thead>
            <tbody>
              @for (r of tb.data; track r.accountId) {
                <tr>
                  <td class="num">{{ r.code }}</td>
                  <td>{{ r.name }}</td>
                  <td class="muted max-md:hidden">{{ typeLabel[r.type] }}</td>
                  <td class="num" nzAlign="right">{{ r.periodDebit ? (r.periodDebit | number: '1.2-2') : '' }}</td>
                  <td class="num" nzAlign="right">{{ r.periodCredit ? (r.periodCredit | number: '1.2-2') : '' }}</td>
                  <td class="num" nzAlign="right">{{ r.balanceDebit ? (r.balanceDebit | number: '1.2-2') : '' }}</td>
                  <td class="num" nzAlign="right">{{ r.balanceCredit ? (r.balanceCredit | number: '1.2-2') : '' }}</td>
                </tr>
              }
              @if (t.rows.length) {
                <tr class="total">
                  <td></td>
                  <td>รวม</td>
                  <td class="max-md:hidden"></td>
                  <td class="num" nzAlign="right">{{ sum(t, 'periodDebit') | number: '1.2-2' }}</td>
                  <td class="num" nzAlign="right">{{ sum(t, 'periodCredit') | number: '1.2-2' }}</td>
                  <td class="num" nzAlign="right">{{ sum(t, 'balanceDebit') | number: '1.2-2' }}</td>
                  <td class="num" nzAlign="right">{{ sum(t, 'balanceCredit') | number: '1.2-2' }}</td>
                </tr>
              }
            </tbody>
          </nz-table>
        </nz-card>
        <nz-alert
          nzType="info"
          nzShowIcon
          nzMessage="ยอดยกไป = ยอดสะสมตั้งแต่เอกสารแรกในระบบจนถึงสิ้นงวดนี้ (ยังไม่รวมยอดยกมาก่อนเริ่มใช้ระบบ)"
        />
      }
    </div>
  `,
  styles: `
    .entry td { background: var(--surface-2); font-weight: 500; }
    .entry a { color: var(--ink); }
    .entry a:hover { text-decoration: underline; }
    .kind { margin-left: 8px; font-weight: 400; }
    .line td { border-bottom-color: transparent !important; padding-top: 4px !important; padding-bottom: 4px !important; }
    .total td { font-weight: 600; border-top: 2px solid var(--line-strong); }
  `,
})
export class JournalPage {
  readonly periodParam = input<string | undefined>(undefined, { alias: 'period' });

  private readonly api = inject(ReceiptApi);
  private readonly router = inject(Router);
  private readonly store = inject(CompanyStore);

  protected readonly typeLabel = ACCOUNT_TYPE_LABEL;
  protected readonly label = periodLabel;
  protected readonly views: { value: 'journal' | 'trial'; label: string }[] = [
    { value: 'journal', label: 'สมุดรายวัน' },
    { value: 'trial', label: 'งบทดลอง' },
  ];
  protected readonly period = signal(currentPeriod());
  protected readonly periodDate = computed(() => toDate(this.period()));
  protected readonly view = signal<'journal' | 'trial'>('journal');
  protected readonly journal = signal<Journal | null>(null);
  protected readonly trial = signal<TrialBalance | null>(null);
  protected readonly csvUrl = computed(() => {
    const c = this.store.current();
    return c ? this.api.journalCsvUrl(c.id, this.period()) : null;
  });

  constructor() {
    effect(() => {
      const p = this.periodParam();
      if (p) untracked(() => this.period.set(p));
    });
    effect(() => {
      const c = this.store.current();
      const p = this.period();
      if (!c) return;
      untracked(() => {
        this.api.journal(c.id, p).subscribe((j) => this.journal.set(j));
        this.api.trialBalance(c.id, p).subscribe((t) => this.trial.set(t));
      });
    });
  }

  protected setPeriod(d: Date | null) {
    const p = fromMonth(d);
    if (!p) return;
    this.period.set(p);
    this.router.navigate([], { queryParams: { period: p }, replaceUrl: true });
  }

  protected sum(t: TrialBalance, key: 'periodDebit' | 'periodCredit' | 'balanceDebit' | 'balanceCredit') {
    return t.rows.reduce((a, r) => a + r[key], 0);
  }
}
