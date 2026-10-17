import { DatePipe, DecimalPipe, JsonPipe, PercentPipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzProgressModule } from 'ng-zorro-antd/progress';
import { NzStatisticModule } from 'ng-zorro-antd/statistic';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { CompanyStore } from '../../core/company-store';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import { injectConfirm } from '../../core/confirm';
import { Accuracy, Example, Issuer } from '../../core/models';

@Component({
  selector: 'app-ai',
  imports: [
    DecimalPipe,
    DatePipe,
    PercentPipe,
    JsonPipe,
    RouterLink,
    FormsModule,
    NzButtonModule,
    NzCardModule,
    NzCheckboxModule,
    NzEmptyModule,
    NzIconModule,
    NzModalModule,
    NzProgressModule,
    NzStatisticModule,
    NzTableModule,
    NzTagModule,
    NzTooltipModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">Training AI</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            AI gets better from documents you've reviewed and approved: The system stores the latest receipt from each issuer as an "example"
            New receipts from existing issuers that fail the rules will be re-read with the example attached for reference · You can pin the clearest and most accurate receipts
          </p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <label nz-checkbox [ngModel]="thisCompany()" (ngModelChange)="thisCompany.set($event)">เฉพาะบริษัทนี้</label>
          <a nz-button nzType="primary" [href]="datasetUrl()" download><nz-icon nzType="download" /> ดาวน์โหลด dataset (.zip)</a>
        </div>
      </header>

      @if (accuracy(); as a) {
        <div class="grid grid-cols-[repeat(auto-fit,minmax(190px,1fr))] gap-3">
          <nz-card>
            <nz-statistic [nzValue]="a.documents ? (a.fieldRate | percent: '1.0-1')! : '—'" nzTitle="ความแม่นยำรายช่อง" />
            <div class="muted sub mt-1">จาก {{ a.documents }} เอกสารที่อนุมัติ</div>
          </nz-card>
          <nz-card>
            <nz-statistic
              [nzValue]="a.documents ? (a.perfect / a.documents | percent: '1.0-0')! : '—'"
              nzTitle="อ่านถูกทั้งใบ (ไม่ต้องแก้)"
            />
            <div class="muted sub mt-1">{{ a.perfect }} / {{ a.documents }} ใบ</div>
          </nz-card>
          @for (m of a.models; track m.model) {
            <nz-card>
              <nz-statistic [nzValue]="(m.fieldRate | percent: '1.0-1')!" [nzTitle]="m.model" />
              <div class="muted sub mt-1">{{ m.documents }} ใบ · ถูกทั้งใบ {{ m.perfect }}</div>
            </nz-card>
          }
        </div>

        <div class="grid grid-cols-[repeat(auto-fit,minmax(340px,1fr))] gap-4">
          <nz-card nzTitle="ช่องที่ AI อ่านพลาดบ่อย">
            @if (a.documents === 0) {
              <nz-empty nzNotFoundContent="ยังไม่มีข้อมูล — อนุมัติเอกสารที่ AI อ่านแล้วระบบจะเทียบสิ่งที่ AI ตอบกับค่าที่คุณแก้" />
            } @else {
              @for (f of sortedFields(); track f.field) {
                <div class="grid grid-cols-[170px_1fr] items-center gap-2.5 py-0.5">
                  <span>{{ f.label }}</span>
                  <nz-progress
                    [nzPercent]="round(f.rate * 100)"
                    nzSize="small"
                    nzStatus="normal"
                    [nzStrokeColor]="f.rate < 0.8 ? '#faad14' : '#1a7a56'"
                  />
                </div>
              }
            }
          </nz-card>

          <nz-card nzTitle="แนวโน้มตามเดือนที่อนุมัติ">
            @for (t of a.trend; track t.period) {
              <div class="grid grid-cols-[170px_1fr] items-center gap-2.5 py-0.5">
                <span class="num">{{ t.period }} · {{ t.documents }} ใบ</span>
                <nz-progress [nzPercent]="round(t.fieldRate * 100)" nzSize="small" nzStatus="normal" nzStrokeColor="#1a7a56" />
              </div>
            } @empty {
              <nz-empty nzNotFoundContent="ยังไม่มีข้อมูล" />
            }
          </nz-card>
        </div>

        @if (a.mostCorrected.length) {
          <nz-card nzTitle="เอกสารที่ต้องแก้มากที่สุด" [nzBodyStyle]="{ padding: '0' }">
            <nz-table #mc nzSize="middle" [nzData]="a.mostCorrected" [nzFrontPagination]="false" [nzShowPagination]="false">
              <thead>
                <tr><th>ไฟล์</th><th>คู่ค้า</th><th>ช่องที่แก้</th><th class="max-md:hidden">โมเดล</th></tr>
              </thead>
              <tbody>
                @for (d of mc.data; track d.id) {
                  <tr>
                    <td><a [routerLink]="['/receipts', d.id]">{{ d.originalFileName }}</a></td>
                    <td>{{ d.counterpartyName ?? '—' }}</td>
                    <td>
                      @for (w of d.wrongFields; track w) {
                        <nz-tag nzColor="warning">{{ w }}</nz-tag>
                      }
                    </td>
                    <td class="muted max-md:hidden">{{ d.model }}</td>
                  </tr>
                }
              </tbody>
            </nz-table>
          </nz-card>
        }
      }

      <nz-card nzTitle="ตัวอย่างที่ใช้สอน AI (ตามผู้ออกเอกสาร)" [nzBodyStyle]="{ padding: '0' }">
        <p class="muted explain m-0 px-4 py-3">
          ใช้ตอนอ่านซ้ำเมื่อรอบแรกไม่ผ่านการตรวจสอบ · ใบที่ปักหมุด (★) จะถูกส่งก่อนเสมอและไม่ถูกแทนที่ด้วยใบใหม่ ·
          ลบตัวอย่างที่ผิดออกเพื่อไม่ให้ AI จำผิด · เพิ่มได้จากปุ่ม “ใช้ Training AI” ในหน้าตรวจเอกสารที่อนุมัติแล้ว
        </p>
        <nz-table #iss nzSize="middle" [nzData]="issuers()" [nzFrontPagination]="false" [nzShowPagination]="false" [nzNoResult]="noIssuers">
          <thead>
            <tr><th>ผู้ออกเอกสาร</th><th class="max-md:hidden">เลขผู้เสียภาษี</th><th nzAlign="right">อนุมัติ</th><th>ตัวอย่าง</th></tr>
          </thead>
          <tbody>
            @for (i of iss.data; track i.id) {
              <tr>
                <td class="strong">{{ i.name }}</td>
                <td class="num max-md:hidden">{{ i.taxId }}</td>
                <td class="num" nzAlign="right">{{ i.approvedReceiptCount }}</td>
                <td>
                  <div class="flex flex-wrap gap-1.5">
                    @for (e of i.examples; track e.id) {
                      <nz-tag [nzColor]="e.pinned ? 'green' : 'default'" class="m-0 inline-flex items-center gap-1.5">
                        <a [routerLink]="['/receipts', e.receiptId]" class="num">{{ e.documentNumber ?? (e.createdAt | date: 'dd/MM') }}</a>
                        @if (e.total != null) {
                          <span class="muted num">{{ e.total | number: '1.0-2' }}</span>
                        }
                        <nz-icon nzType="code" class="act" nz-tooltip nzTooltipTitle="ดู JSON" (click)="view(e)" />
                        <nz-icon
                          nzType="star"
                          [nzTheme]="e.pinned ? 'fill' : 'outline'"
                          class="act"
                          nz-tooltip
                          [nzTooltipTitle]="e.pinned ? 'เลิกปักหมุด' : 'ปักหมุด'"
                          (click)="togglePin(e)"
                        />
                        <nz-icon
                          nzType="delete"
                          class="act danger"
                          nz-tooltip
                          nzTooltipTitle="ลบตัวอย่าง"
                          (click)="askRemove(e)"
                        />
                      </nz-tag>
                    } @empty {
                      <span class="muted">—</span>
                    }
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </nz-table>
        <ng-template #noIssuers>ยังไม่มี — อนุมัติเอกสารที่มีเลขผู้เสียภาษีผู้ขายถูกต้องแล้วจะมาอยู่ที่นี่</ng-template>
      </nz-card>

      <nz-card nzTitle="อยากให้แม่นขึ้นอีก (fine-tune)">
        <ol class="m-0 grid gap-1 pl-5">
          <li>อนุมัติเอกสารจริงให้ได้หลายร้อยใบ (ยิ่งหลากหลายผู้ออกยิ่งดี) แล้วกด “ดาวน์โหลด dataset”</li>
          <li>ไฟล์ zip มีรูปต้นฉบับ + train.jsonl (คำตอบที่ถูกต้องที่คุณอนุมัติ) ใช้กับ Unsloth / LLaMA-Factory บน GPU</li>
          <li>แปลงผลเป็น GGUF แล้ว <code>ollama create</code> เป็นโมเดลใหม่ ตั้ง <code>Extraction:VisionModel</code> ให้ชี้ไปที่โมเดลนั้น</li>
          <li>กลับมาดูหน้านี้ว่าความแม่นยำรายโมเดลดีขึ้นจริงไหม</li>
        </ol>
      </nz-card>
    </div>

    <nz-modal [nzVisible]="viewing() !== null" nzTitle="JSON ตัวอย่าง" [nzFooter]="null" nzWidth="720px" (nzOnCancel)="viewing.set(null)">
      <ng-container *nzModalContent>
        <pre>{{ viewing() | json }}</pre>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .sub { font-size: 13px; }
    .strong { font-weight: 500; }
    .explain { font-size: 13px; }
    .act { cursor: pointer; color: var(--ink-3); }
    .act:hover { color: var(--ink); }
    .act.danger:hover { color: var(--error); }
    pre { max-height: 60vh; overflow: auto; font-family: var(--mono); font-size: 12px; margin: 0; }
    code { font-family: var(--mono); font-size: 12px; background: var(--surface-2); padding: 1px 4px; border-radius: 4px; }
  `,
})
export class AiPage {
  private readonly api = inject(ReceiptApi);
  private readonly store = inject(CompanyStore);
  private readonly message = inject(NzMessageService);
  private readonly confirm = injectConfirm();

  protected readonly thisCompany = signal(false);
  protected readonly accuracy = signal<Accuracy | null>(null);
  protected readonly issuers = signal<Issuer[]>([]);
  protected readonly viewing = signal<unknown>(null);

  private readonly companyFilter = computed(() => (this.thisCompany() ? (this.store.current()?.id ?? null) : null));
  protected readonly datasetUrl = computed(() => this.api.datasetUrl(this.companyFilter()));
  protected readonly sortedFields = computed(() => [...(this.accuracy()?.fields ?? [])].sort((a, b) => a.rate - b.rate));

  constructor() {
    effect(() => {
      const c = this.companyFilter();
      untracked(() => this.api.accuracy(c).subscribe((a) => this.accuracy.set(a)));
    });
    this.loadIssuers();
  }

  protected round(n: number) {
    return Math.round(n);
  }

  protected view(e: Example) {
    this.api.exampleJson(e.id).subscribe((j) => this.viewing.set(j));
  }

  protected togglePin(e: Example) {
    this.api.setPinned(e.id, !e.pinned).subscribe({
      next: () => this.loadIssuers(),
      error: (err) => this.message.error(errorMessage(err)),
    });
  }

  protected askRemove(e: Example) {
    this.confirm({
      title: 'ลบตัวอย่างนี้?',
      content: 'เอกสารยังอยู่ แค่ไม่ใช้สอน AI',
      okText: 'ลบ',
      danger: true,
      onOk: () => this.remove(e),
    });
  }

  protected remove(e: Example) {
    this.api.deleteExample(e.id).subscribe({
      next: () => this.loadIssuers(),
      error: (err) => this.message.error(errorMessage(err)),
    });
  }

  private loadIssuers() {
    this.api.issuers().subscribe((i) => this.issuers.set(i));
  }
}
