import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { Observable } from 'rxjs';
import { CompanyStore } from '../../core/company-store';
import { injectConfirm } from '../../core/confirm';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import { PeriodSummary, periodLabel } from '../../core/models';

@Component({
  selector: 'app-periods',
  imports: [
    DecimalPipe,
    DatePipe,
    FormsModule,
    RouterLink,
    NzAlertModule,
    NzButtonModule,
    NzCardModule,
    NzIconModule,
    NzModalModule,
    NzSelectModule,
    NzTableModule,
    NzTagModule,
    NzTooltipModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">งวดบัญชี (รายเดือน)</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            เอกสารแต่ละใบอยู่ในงวดตามเดือนของวันที่เอกสาร — ย้ายงวดได้ในหน้าตรวจ (เช่น ใบกำกับภาษีซื้อมาช้า
            ขอคืนภาษีเดือนถัดไป ได้ไม่เกิน 6 เดือน) · ปิดงวดแล้วเอกสารและการชำระเงินในเดือนนั้นแก้ไขไม่ได้
          </p>
        </div>
        <nz-select
          class="w-40 max-sm:w-full"
          nzAllowClear
          nzPlaceHolder="ทุกปี"
          [ngModel]="year()"
          (ngModelChange)="year.set($event ?? null)"
          aria-label="ปี"
        >
          @for (y of years(); track y) {
            <nz-option [nzValue]="y" [nzLabel]="'ปี ' + (y + 543)"></nz-option>
          }
        </nz-select>
      </header>

      <nz-card [nzBodyStyle]="{ padding: '0' }">
        <nz-table #t nzSize="middle" [nzData]="shown()" [nzFrontPagination]="false" [nzShowPagination]="false" [nzLoading]="loading()">
          <thead>
            <tr>
              <th>งวด</th>
              <th nzAlign="right">รายจ่าย (ใบ)</th>
              <th nzAlign="right">ภาษีซื้อ</th>
              <th nzAlign="right">รายรับ (ใบ)</th>
              <th nzAlign="right">ภาษีขาย</th>
              <th nzAlign="right">VAT ต้องชำระ</th>
              <th class="max-md:hidden" nzAlign="right">จ่ายออก</th>
              <th class="max-md:hidden" nzAlign="right">รับเข้า</th>
              <th>สถานะ</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (p of t.data; track p.period) {
              <tr>
                <td class="strong">{{ label(p.period) }}</td>
                <td class="num" nzAlign="right">
                  <a [routerLink]="['/receipts']" [queryParams]="{ direction: 'Purchase', period: p.period }">
                    {{ p.purchases.approved }}/{{ p.purchases.count }}
                  </a>
                </td>
                <td class="num" nzAlign="right">{{ p.purchases.vat | number: '1.2-2' }}</td>
                <td class="num" nzAlign="right">
                  <a [routerLink]="['/receipts']" [queryParams]="{ direction: 'Sale', period: p.period }">
                    {{ p.sales.approved }}/{{ p.sales.count }}
                  </a>
                </td>
                <td class="num" nzAlign="right">{{ p.sales.vat | number: '1.2-2' }}</td>
                <td class="num strong" nzAlign="right" [class.refund]="p.vatPayable < 0">
                  {{ p.vatPayable | number: '1.2-2' }}
                </td>
                <td class="num max-md:hidden" nzAlign="right">{{ p.paymentsOut | number: '1.2-2' }}</td>
                <td class="num max-md:hidden" nzAlign="right">{{ p.paymentsIn | number: '1.2-2' }}</td>
                <td>
                  @if (p.closed) {
                    <nz-tag nz-tooltip [nzTooltipTitle]="'ปิดเมื่อ ' + (p.closedAt | date: 'dd/MM/yyyy HH:mm')">
                      <nz-icon nzType="lock" /> ปิดแล้ว
                    </nz-tag>
                  } @else if (p.pendingCount) {
                    <nz-tag nzColor="warning">รออนุมัติ {{ p.pendingCount }}</nz-tag>
                  } @else {
                    <nz-tag nzColor="success">เปิด</nz-tag>
                  }
                </td>
                <td class="whitespace-nowrap" nzAlign="right">
                  <a nz-button nzType="link" nzSize="small" routerLink="/journal" [queryParams]="{ period: p.period }">สมุดรายวัน</a>
                  @if (p.closed) {
                    <button nz-button nzSize="small" (click)="reopen(p)" [disabled]="busy()">
                      <nz-icon nzType="unlock" /> เปิดงวด
                    </button>
                  } @else {
                    <button
                      nz-button
                      nzSize="small"
                      [disabled]="busy() || p.pendingCount > 0"
                      nz-tooltip
                      [nzTooltipTitle]="p.pendingCount ? 'อนุมัติ/ลบ/ย้ายงวดเอกสารที่ค้างก่อน' : ''"
                      (click)="askClose(p)"
                    >
                      <nz-icon nzType="lock" /> ปิดงวด
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </nz-table>
      </nz-card>
      <nz-alert
        nzType="info"
        nzShowIcon
        nzMessage="ตัวเลขนับเฉพาะเอกสารที่อนุมัติแล้ว · ซื้อ/ขาย แสดง อนุมัติ/ทั้งหมด · ภาษีซื้อนับเฉพาะใบกำกับเต็มรูป (อย่างย่อขอคืนไม่ได้) · VAT ต้องชำระ = ภาษีขาย − ภาษีซื้อ (ติดลบ = ขอคืน/ยกไป)"
      />
    </div>
  `,
  styles: `
    .strong { font-weight: 600; }
    .refund { color: var(--accent); }
  `,
})
export class Periods {
  private readonly api = inject(ReceiptApi);
  private readonly store = inject(CompanyStore);
  private readonly message = inject(NzMessageService);
  private readonly confirm = injectConfirm();

  protected readonly periods = signal<PeriodSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly label = periodLabel;

  /** Calendar year shown (ค.ศ.; labelled in พ.ศ.), null = every year. Opens on this year. */
  protected readonly year = signal<number | null>(new Date().getFullYear());
  /** Years that have a period, plus this year and the selected one, newest first. */
  protected readonly years = computed(() => {
    const set = new Set(this.periods().map((p) => Number(p.period.slice(0, 4))));
    set.add(new Date().getFullYear());
    const selected = this.year();
    if (selected) set.add(selected);
    return [...set].sort((a, b) => b - a);
  });
  protected readonly shown = computed(() => {
    const y = this.year();
    return y == null ? this.periods() : this.periods().filter((p) => p.period.startsWith(`${y}-`));
  });

  constructor() {
    effect(() => {
      const c = this.store.current();
      if (c) untracked(() => this.load());
    });
  }

  protected askClose(p: PeriodSummary) {
    this.confirm({
      title: `ปิดงวด ${this.label(p.period)}?`,
      content: 'เอกสารและการชำระเงินในเดือนนี้จะแก้ไขไม่ได้จนกว่าจะเปิดงวด',
      okText: 'ปิดงวด',
      onOk: () => this.close(p),
    });
  }

  protected close(p: PeriodSummary) {
    this.run(this.api.closePeriod(this.store.current()!.id, p.period), 'ปิดงวดแล้ว');
  }

  protected reopen(p: PeriodSummary) {
    this.run(this.api.reopenPeriod(this.store.current()!.id, p.period), 'เปิดงวดแล้ว');
  }

  private run(req: Observable<void>, done: string) {
    this.busy.set(true);
    req.subscribe({
      next: () => {
        this.busy.set(false);
        this.message.success(done);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  private load() {
    const c = this.store.current();
    if (!c) return;
    this.loading.set(true);
    this.api.periods(c.id).subscribe({
      next: (p) => {
        this.periods.set(p);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
