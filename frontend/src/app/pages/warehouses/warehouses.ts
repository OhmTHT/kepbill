import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { ActiveMark } from '../../core/active-mark';
import { Auth } from '../../core/auth';
import { CompanyStore } from '../../core/company-store';
import { injectConfirm } from '../../core/confirm';
import { Warehouse, WarehouseInput } from '../../core/models';
import { ReceiptApi, errorMessage, stockLoadError } from '../../core/receipt-api';

@Component({
  selector: 'app-warehouses',
  imports: [
    ReactiveFormsModule,
    ActiveMark,
    NzButtonModule,
    NzCardModule,
    NzCheckboxModule,
    NzFormModule,
    NzIconModule,
    NzInputModule,
    NzModalModule,
    NzTableModule,
    NzTagModule,
    NzTooltipModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-240 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">คลังสินค้า</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            ที่เก็บสินค้า เช่น โกดัง หน้าร้าน สาขา · เอกสารซื้อขายจะรับเข้า/ตัดออกจาก<b>คลังหลัก</b> เว้นแต่เลือกคลังอื่น
          </p>
        </div>
        @if (auth.isManager()) {
          <button nz-button nzType="primary" (click)="open(null)"><nz-icon nzType="plus" /> เพิ่มคลัง</button>
        }
      </header>

      <nz-card [nzBodyStyle]="{ padding: '0' }">
        <nz-table #wt nzSize="small" [nzData]="warehouses()" [nzShowPagination]="false" [nzFrontPagination]="false" [nzLoading]="!loaded()">
          <thead>
            <tr>
              <th>ชื่อคลัง</th>
              <th nzWidth="64px" nzAlign="center">สถานะ</th>
              @if (auth.isManager()) {
                <th nzWidth="260px"></th>
              }
            </tr>
          </thead>
          <tbody>
            @for (w of wt.data; track w.id) {
              <tr [class.inactive]="!w.active">
                <td>
                  {{ w.name }}
                  @if (w.isDefault) {
                    <nz-tag class="ml-1" nzColor="green">คลังหลัก</nz-tag>
                  }
                </td>
                <td nzAlign="center"><app-active-mark [active]="w.active" /></td>
                @if (auth.isManager()) {
                  <td>
                    <div class="flex items-center justify-end gap-1 whitespace-nowrap">
                      @if (!w.isDefault && w.active) {
                        <button nz-button nzType="text" nzSize="small" (click)="makeDefault(w)">ตั้งเป็นคลังหลัก</button>
                      }
                      <button nz-button nzType="text" nzSize="small" (click)="open(w)"><nz-icon nzType="edit" /> แก้ไข</button>
                      <span class="inline-flex w-7 justify-center">
                        @if (w.isDefault) {
                          <nz-icon class="muted" nzType="lock" nz-tooltip nzTooltipTitle="คลังหลัก ลบไม่ได้" />
                        } @else {
                          <button nz-button nzType="text" nzDanger nzSize="small" (click)="askDelete(w)"
                                  nz-tooltip nzTooltipTitle="ลบ" [attr.aria-label]="'ลบคลัง ' + w.name">
                            <nz-icon nzType="delete" />
                          </button>
                        }
                      </span>
                    </div>
                  </td>
                }
              </tr>
            }
          </tbody>
        </nz-table>
      </nz-card>
    </div>

    <nz-modal
      [nzVisible]="formOpen()"
      [nzTitle]="editing() ? 'แก้ไขคลัง' : 'เพิ่มคลัง'"
      nzOkText="บันทึก"
      [nzOkDisabled]="form.invalid"
      [nzOkLoading]="busy()"
      (nzOnOk)="save()"
      (nzOnCancel)="formOpen.set(false)"
    >
      <ng-container *nzModalContent>
        <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
          <nz-form-item>
            <nz-form-label nzRequired>ชื่อคลัง</nz-form-label>
            <nz-form-control><input nz-input formControlName="name" maxlength="200" placeholder="เช่น โกดังบางนา, หน้าร้าน" /></nz-form-control>
          </nz-form-item>
          @if (!editing()?.isDefault) {
            <nz-form-item class="mb-0">
              <label nz-checkbox formControlName="active">เปิดใช้งาน</label>
              <div class="muted text-[12px]">ปิดเมื่อเลิกใช้คลังนี้ ประวัติยังอยู่</div>
            </nz-form-item>
          }
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .inactive td { color: var(--ink-3); }
  `,
})
export class Warehouses {
  private readonly api = inject(ReceiptApi);
  private readonly fb = inject(FormBuilder);
  private readonly message = inject(NzMessageService);
  private readonly confirm = injectConfirm();
  private readonly store = inject(CompanyStore);
  protected readonly auth = inject(Auth);

  protected readonly warehouses = signal<Warehouse[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Warehouse | null>(null);
  protected readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    active: true,
  });

  constructor() {
    effect(() => {
      const companyId = this.store.current()?.id;
      untracked(() => {
        if (companyId) this.load(companyId);
      });
    });
  }

  private get companyId(): string | null {
    return this.store.current()?.id ?? null;
  }

  private load(companyId = this.companyId) {
    if (!companyId) return;
    this.api.warehouses(companyId).subscribe({
      next: (w) => {
        this.warehouses.set(w);
        this.loaded.set(true);
      },
      error: (e) => {
        this.loaded.set(true);
        this.message.error(stockLoadError(e));
      },
    });
  }

  protected open(w: Warehouse | null) {
    this.editing.set(w);
    this.form.reset({ name: w?.name ?? '', active: w?.active ?? true });
    this.formOpen.set(true);
  }

  protected save() {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const w = this.editing();
    this.send(w, { name: v.name.trim(), isDefault: w?.isDefault ?? false, active: w?.isDefault ? true : v.active }, () =>
      this.formOpen.set(false),
    );
  }

  protected makeDefault(w: Warehouse) {
    this.send(w, { name: w.name, isDefault: true, active: true }, () =>
      this.message.success(`${w.name} เป็นคลังหลักแล้ว`),
    );
  }

  private send(w: Warehouse | null, body: WarehouseInput, done: () => void) {
    const companyId = this.companyId;
    if (!companyId || this.busy()) return;
    const req = w ? this.api.updateWarehouse(companyId, w.id, body) : this.api.createWarehouse(companyId, body);
    this.busy.set(true);
    req.subscribe({
      next: () => {
        this.busy.set(false);
        done();
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected askDelete(w: Warehouse) {
    this.confirm({
      title: `ลบคลัง ${w.name} ?`,
      okText: 'ลบ',
      danger: true,
      onOk: () => {
        const companyId = this.companyId;
        if (!companyId) return;
        this.api.deleteWarehouse(companyId, w.id).subscribe({
          next: () => {
            this.message.success(`ลบ ${w.name} แล้ว`);
            this.load();
          },
          error: (e) => this.message.error(errorMessage(e)),
        });
      },
    });
  }
}
