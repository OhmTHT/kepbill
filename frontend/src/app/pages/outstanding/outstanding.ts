import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzStatisticModule } from 'ng-zorro-antd/statistic';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CompanyStore } from '../../core/company-store';
import { injectConfirm } from '../../core/confirm';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import { Direction, Outstanding, OutstandingItem, today } from '../../core/models';

@Component({
  selector: 'app-outstanding',
  imports: [
    DecimalPipe,
    DatePipe,
    NzButtonModule,
    NzCardModule,
    NzIconModule,
    NzModalModule,
    NzStatisticModule,
    NzTableModule,
    NzTagModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">
            {{ direction() === 'Purchase' ? 'เจ้าหนี้ค้างจ่าย' : 'ลูกหนี้ค้างรับ' }}
          </h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            เอกสารที่อนุมัติแล้วแต่ยังชำระไม่ครบ — เช่น วางบิลเดือน 1 จ่ายจริงเดือน 3:
            ค่าใช้จ่ายลงเดือน 1 เป็นเจ้าหนี้ไว้ แล้วตัดเจ้าหนี้ในเดือนที่จ่ายจริง ·
            ใบเสร็จรับเงินถือว่าชำระแล้วอัตโนมัติ
          </p>
        </div>
        <div class="seg max-sm:w-full" role="tablist" aria-label="ค้างจ่าย / ค้างรับ">
          @for (o of options; track o.value) {
            <button
              type="button"
              role="tab"
              class="seg-btn"
              [class.active]="direction() === o.value"
              [attr.aria-selected]="direction() === o.value"
              (click)="select(o.value)"
            >
              <nz-icon [nzType]="o.icon" />
              <span>{{ o.label }}</span>
            </button>
          }
        </div>
      </header>

      @if (data(); as d) {
        <div class="grid grid-cols-[repeat(auto-fit,minmax(190px,1fr))] gap-3">
          <nz-card>
            <nz-statistic [nzValue]="(d.total | number: '1.2-2')!" [nzTitle]="'รวมค้าง' + verb()" />
            <div class="muted sub mt-1">{{ d.items.length }} เอกสาร</div>
          </nz-card>
          @for (b of bucketNames(); track b; let first = $first) {
            <nz-card>
              <nz-statistic
                [nzValue]="(d.buckets[b] | number: '1.2-2')!"
                [nzTitle]="first ? b : 'เกินกำหนด ' + b"
                [nzValueStyle]="!first && d.buckets[b] > 0 ? { color: '#c4321f' } : {}"
              />
            </nz-card>
          }
        </div>

        <nz-card [nzBodyStyle]="{ padding: '0' }">
          <nz-table
            #t
            nzSize="middle"
            [nzData]="d.items"
            [nzPageSize]="50"
            [nzHideOnSinglePage]="true"
          >
            <thead>
              <tr>
                <th>{{ direction() === 'Purchase' ? 'ผู้ขาย' : 'ผู้ซื้อ' }}</th>
                <th class="max-md:hidden">เลขที่</th>
                <th>วันที่</th>
                <th>ครบกำหนด</th>
                <th class="max-md:hidden" nzAlign="right">ยอดเอกสาร</th>
                <th class="max-md:hidden" nzAlign="right">ชำระแล้ว</th>
                <th nzAlign="right">คงค้าง</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (i of t.data; track i.id) {
                <tr class="click" (click)="open(i)">
                  <td>{{ i.counterpartyName ?? '—' }}</td>
                  <td class="num max-md:hidden">{{ i.documentNumber ?? '—' }}</td>
                  <td class="num">{{ i.issueDate | date: 'dd/MM/yyyy' }}</td>
                  <td class="num whitespace-nowrap">
                    {{ i.dueDate ? (i.dueDate | date: 'dd/MM/yyyy') : '—' }}
                    @if (i.daysOverdue > 0) {
                      <nz-tag nzColor="error">เกิน {{ i.daysOverdue }} วัน</nz-tag>
                    }
                  </td>
                  <td class="num max-md:hidden" nzAlign="right">
                    {{ i.total + i.adjustments | number: '1.2-2' }}
                  </td>
                  <td class="num max-md:hidden" nzAlign="right">{{ i.paid | number: '1.2-2' }}</td>
                  <td class="num strong" nzAlign="right">{{ i.outstanding | number: '1.2-2' }}</td>
                  <td nzAlign="right" (click)="$event.stopPropagation()">
                    <button nz-button nzSize="small" (click)="askPayInFull(i)" [disabled]="busy()">
                      {{ direction() === 'Purchase' ? 'จ่ายครบ' : 'รับครบ' }}
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
        </nz-card>
        <p class="muted note m-0">
          ต้องการกำหนดวันที่หรือบัญชีเอง ให้เปิดเอกสารแล้วบันทึกที่ส่วนการชำระเงิน
        </p>
      }
    </div>
  `,
  styles: `
    .sub {
      font-size: 13px;
    }
    .strong {
      font-weight: 600;
    }
    .note {
      font-size: 13px;
    }
  `,
})
export class OutstandingPage {
  private readonly api = inject(ReceiptApi);
  private readonly router = inject(Router);
  private readonly message = inject(NzMessageService);
  private readonly store = inject(CompanyStore);
  private readonly confirm = injectConfirm();

  protected readonly options: { value: Direction; label: string; icon: string }[] = [
    { value: 'Purchase', label: 'ค้างจ่าย (เจ้าหนี้)', icon: 'shopping-cart' },
    { value: 'Sale', label: 'ค้างรับ (ลูกหนี้)', icon: 'shop' },
  ];
  protected readonly direction = signal<Direction>('Purchase');
  protected readonly data = signal<Outstanding | null>(null);
  protected readonly busy = signal(false);
  protected readonly verb = computed(() => (this.direction() === 'Purchase' ? 'จ่าย' : 'รับ'));
  protected readonly bucketNames = computed(() => Object.keys(this.data()?.buckets ?? {}));

  constructor() {
    effect(() => {
      const c = this.store.current();
      if (c) untracked(() => this.load());
    });
  }

  protected select(d: Direction) {
    this.direction.set(d);
    this.load();
  }

  protected open(i: OutstandingItem) {
    this.router.navigate(['/receipts', i.id]);
  }

  protected askPayInFull(i: OutstandingItem) {
    const verb = this.direction() === 'Purchase' ? 'จ่าย' : 'รับ';
    const baht = (n: number) =>
      n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const wht = i.whtRemaining > 0 ? ` · หัก ณ ที่จ่าย ${baht(i.whtRemaining)} บาท` : '';
    this.confirm({
      title: `บันทึก${verb}เงิน ${baht(i.outstanding - i.whtRemaining)} บาท?`,
      content: `ลงวันที่วันนี้ ผ่านบัญชีธนาคาร${wht}`,
      okText: `บันทึก${verb}เงิน`,
      onOk: () => this.payInFull(i),
    });
  }

  protected payInFull(i: OutstandingItem) {
    const bank = this.store.accounts().find((a) => a.role === 'Bank');
    this.busy.set(true);
    const amount = Math.round((i.outstanding - i.whtRemaining) * 100) / 100;
    this.api
      .addPayment(i.id, {
        date: today(),
        amount,
        accountId: bank?.id ?? null,
        note: null,
        whtAmount: i.whtRemaining,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.message.success('บันทึกการชำระแล้ว');
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
    if (c) this.api.outstanding(c.id, this.direction()).subscribe((d) => this.data.set(d));
  }
}
