import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzGridModule } from 'ng-zorro-antd/grid';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { Auth } from '../../core/auth';
import { CompanyStore } from '../../core/company-store';
import { injectConfirm } from '../../core/confirm';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import { ActiveMark } from '../../core/active-mark';
import { ACCOUNT_ROLE_LABEL, ACCOUNT_TYPE_LABEL, Account, AccountType, Company } from '../../core/models';

/** First digit of an account code per type, by the usual Thai chart convention. */
const ACCOUNT_CODE_PREFIX: Record<AccountType, string> = {
  Asset: '1',
  Liability: '2',
  Equity: '3',
  Revenue: '4',
  Expense: '5',
};

@Component({
  selector: 'app-companies',
  imports: [
    NzModalModule,
    ReactiveFormsModule,
    ActiveMark,
    NzButtonModule,
    NzCardModule,
    NzCheckboxModule,
    NzEmptyModule,
    NzFormModule,
    NzGridModule,
    NzIconModule,
    NzInputModule,
    NzSelectModule,
    NzSwitchModule,
    NzTableModule,
    NzTagModule,
    NzTooltipModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">บริษัท</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            ข้อมูลฝั่งเรา (ชื่อ เลขผู้เสียภาษี สาขา ที่อยู่) ถูกเติมให้ทุกเอกสารจากโปรไฟล์นี้ — AI อ่านเฉพาะคู่ค้า
            และใช้เลขผู้เสียภาษีของเราตรวจว่าเอกสารส่งถึงบริษัทนี้จริง
          </p>
        </div>
        @if (auth.isSuperAdmin()) {
          <button nz-button nzType="primary" (click)="startNew()"><nz-icon nzType="plus" /> เพิ่มบริษัท</button>
        }
      </header>

      <div class="grid grid-cols-[300px_minmax(0,1fr)] items-start gap-4 max-[860px]:grid-cols-1">
        <nz-card [nzBodyStyle]="{ padding: '6px' }">
          @if (store.companies().length) {
            <ul class="list m-0 grid list-none gap-0.5 p-0">
              @for (c of store.companies(); track c.id) {
                <li>
                  <button type="button" class="item" [class.selected]="!creating() && editing()?.id === c.id" (click)="edit(c)">
                    <span class="name">{{ c.name }}</span>
                    <span class="meta">
                      <span class="num">{{ c.taxId || 'ยังไม่มีเลขผู้เสียภาษี' }}</span> · {{ c.documentCount }} เอกสาร
                    </span>
                  </button>
                </li>
              }
            </ul>
          } @else {
            <nz-empty nzNotFoundContent="ยังไม่มีบริษัท" />
          }
        </nz-card>

        <div class="grid min-w-0 grid-cols-1 gap-4">
          @if (creating() || editing()) {
            <nz-card [nzTitle]="creating() ? 'บริษัทใหม่' : 'โปรไฟล์บริษัท'" [nzExtra]="useBtn">
              <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="saveCompany()">
                <div nz-row [nzGutter]="12">
                  <nz-form-item nz-col [nzSpan]="24">
                    <nz-form-label nzRequired>ชื่อบริษัท (ตามที่พิมพ์บนใบกำกับ)</nz-form-label>
                    <nz-form-control><input nz-input formControlName="name" placeholder="บริษัท ตัวอย่าง จำกัด" /></nz-form-control>
                  </nz-form-item>
                  <nz-form-item nz-col [nzXs]="24" [nzSm]="12">
                    <nz-form-label>เลขผู้เสียภาษี 13 หลัก</nz-form-label>
                    <nz-form-control><input nz-input class="num" inputmode="numeric" formControlName="taxId" /></nz-form-control>
                  </nz-form-item>
                  <nz-form-item nz-col [nzXs]="24" [nzSm]="12">
                    <nz-form-label>สาขา</nz-form-label>
                    <nz-form-control><input nz-input class="num" formControlName="branch" placeholder="00000 = สำนักงานใหญ่" /></nz-form-control>
                  </nz-form-item>
                  <nz-form-item nz-col [nzSpan]="24">
                    <nz-form-label>ที่อยู่</nz-form-label>
                    <nz-form-control><textarea nz-input rows="2" formControlName="address"></textarea></nz-form-control>
                  </nz-form-item>
                  <nz-form-item nz-col [nzSpan]="24">
                    <label nz-checkbox formControlName="vatRegistered">จดทะเบียนภาษีมูลค่าเพิ่ม (ขอคืนภาษีซื้อ / มีภาษีขาย)</label>
                  </nz-form-item>
                </div>
                <!-- only super admins edit a company's profile; admins see it read-only -->
                @if (auth.isSuperAdmin()) {
                <div class="flex gap-2">
                  @if (!creating() && editing(); as c) {
                    <button nz-button nzDanger type="button" (click)="askDelete(c)"><nz-icon nzType="delete" /> ลบบริษัท</button>
                  }
                  <span class="flex-1"></span>
                  @if (creating()) {
                    <button nz-button type="button" (click)="cancelNew()">ยกเลิก</button>
                  }
                  <button nz-button nzType="primary" type="submit" [nzLoading]="busy()"><nz-icon nzType="save" /> บันทึก</button>
                </div>
                }
              </form>
            </nz-card>
            <ng-template #useBtn>
              @if (!creating() && editing()?.id === store.current()?.id) {
                <nz-tag nzColor="green">กำลังใช้งาน</nz-tag>
              }
            </ng-template>

            @if (!creating() && editing(); as c) {
              <nz-card nzTitle="ผังบัญชี" [nzExtra]="addAccountBtn" [nzBodyStyle]="{ padding: '0' }">
                <ng-template #addAccountBtn>
                  <button nz-button nzType="primary" type="button" (click)="openAddAccount()">
                    <nz-icon nzType="plus" /> เพิ่มบัญชี
                  </button>
                </ng-template>
                <p class="muted explain m-0 px-4 py-3">
                  ทุกเอกสารจะถูกลงบัญชีเป็นรายการ Dr/Cr ตามบัญชีในผังนี้ ·
                  <b>หมวด</b>บอกว่าบัญชีไปอยู่ส่วนไหนของงบ (ค่าใช้จ่าย/รายได้ = กำไรขาดทุน, สินทรัพย์/หนี้สิน/ทุน = งบดุล) ·
                  <b>รหัส</b>ใช้เรียงและอ้างอิง (ตัวแรก 1 สินทรัพย์ 2 หนี้สิน 3 ทุน 4 รายได้ 5 ค่าใช้จ่าย) ·
                  บัญชีที่มีป้ายระบบ ระบบลงให้อัตโนมัติ (เปลี่ยนชื่อ/รหัสได้ แต่ปิดใช้หรือลบไม่ได้) ·
                  บัญชีที่ใช้แล้วลบไม่ได้ ให้ปิดใช้แทน
                </p>
                <nz-table #at nzSize="small" [nzData]="accounts()" [nzFrontPagination]="false" [nzShowPagination]="false" [nzScroll]="{ x: '680px' }">
                  <thead>
                    <tr>
                      <th nzWidth="110px">รหัส</th>
                      <th>ชื่อบัญชี</th>
                      <th nzWidth="150px">หมวด</th>
                      <th class="max-md:hidden">ระบบ</th>
                      <th nzWidth="64px" nzAlign="center">สถานะ</th>
                      <th nzWidth="150px"></th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (a of at.data; track a.id) {
                      @if (editingAccount() === a.id) {
                        <tr [formGroup]="accountForm">
                          <td><input nz-input class="num" formControlName="code" /></td>
                          <td><input nz-input formControlName="name" /></td>
                          <td>
                            <nz-select formControlName="type" class="w-full">
                              @for (t of types; track t[0]) {
                                <nz-option [nzValue]="t[0]" [nzLabel]="t[1]"></nz-option>
                              }
                            </nz-select>
                          </td>
                          <td class="max-md:hidden">
                            @if (a.role !== 'None') {
                              <nz-tag nzColor="blue">{{ roleLabel[a.role] }}</nz-tag>
                            }
                          </td>
                          <td><nz-switch formControlName="active" nzSize="small" /></td>
                          <td>
                            <div class="row-actions flex items-center justify-end gap-1">
                              <button nz-button nzType="primary" nzSize="small" type="button" (click)="saveAccount(a)">บันทึก</button>
                              <button nz-button nzType="text" nzSize="small" type="button" (click)="editingAccount.set(null)">ยกเลิก</button>
                            </div>
                          </td>
                        </tr>
                      } @else {
                        <tr [class.inactive]="!a.active">
                          <td class="num">{{ a.code }}</td>
                          <td>{{ a.name }}</td>
                          <td class="muted">{{ typeLabel[a.type] }}</td>
                          <td class="max-md:hidden">
                            @if (a.role !== 'None') {
                              <nz-tag nzColor="blue">{{ roleLabel[a.role] }}</nz-tag>
                            }
                          </td>
                          <td nzAlign="center"><app-active-mark [active]="a.active" /></td>
                          <td>
                            <!-- fixed slots so the buttons line up whether or not a row can be deleted -->
                            <div class="row-actions flex items-center justify-end gap-1">
                              <button nz-button nzType="text" nzSize="small" type="button" (click)="editAccount(a)">
                                <nz-icon nzType="edit" /> แก้ไข
                              </button>
                              <span class="delete-slot inline-flex justify-center">
                                @if (a.role !== 'None') {
                                  <nz-icon class="muted" nzType="lock" nz-tooltip nzTooltipTitle="บัญชีระบบ ลบไม่ได้" />
                                } @else if (a.usage === 0) {
                                  <button nz-button nzType="text" nzDanger nzSize="small" type="button" (click)="askDeleteAccount(c, a)"
                                          nz-tooltip nzTooltipTitle="ลบ" aria-label="ลบบัญชี">
                                    <nz-icon nzType="delete" />
                                  </button>
                                } @else {
                                  <!-- the span carries the tooltip: a disabled button gets no mouse events -->
                                  <span nz-tooltip [nzTooltipTitle]="'ใช้แล้ว ' + a.usage + ' รายการ ลบไม่ได้ — ปิดใช้แทน'">
                                    <button nz-button nzType="text" nzSize="small" type="button" disabled aria-label="ลบบัญชี">
                                      <nz-icon nzType="delete" />
                                    </button>
                                  </span>
                                }
                              </span>
                            </div>
                          </td>
                        </tr>
                      }
                    }
                  </tbody>
                </nz-table>
              </nz-card>
            }
          } @else {
            <nz-card><nz-empty [nzNotFoundContent]="auth.isSuperAdmin() ? 'เลือกบริษัททางซ้าย หรือกด “เพิ่มบริษัท”' : 'เลือกบริษัททางซ้าย'" /></nz-card>
          }
        </div>
      </div>
    </div>

    <nz-modal
      [nzVisible]="deleting() !== null"
      nzTitle="ลบบริษัท"
      nzOkText="ลบถาวร"
      nzOkDanger
      [nzOkDisabled]="confirmName() !== deleting()?.name"
      [nzOkLoading]="busy()"
      (nzOnOk)="deleteCompany()"
      (nzOnCancel)="deleting.set(null)"
    >
      <ng-container *nzModalContent>
        @if (deleting(); as d) {
          <p class="mt-0">
            จะลบ <b>{{ d.name }}</b> พร้อม<b>เอกสารทั้งหมด {{ d.documentCount }} ใบ</b> การชำระเงิน ผังบัญชี
            งวดบัญชีที่ปิดแล้ว และการผูกกับผู้ใช้งาน — <b class="danger">ย้อนกลับไม่ได้</b>
          </p>
          <p class="muted">ไฟล์ใบเสร็จต้นฉบับยังเก็บไว้ในเครื่องเซิร์ฟเวอร์</p>
          <p class="mb-1">พิมพ์ชื่อบริษัทให้ตรงเพื่อยืนยัน:</p>
          <input nz-input [value]="confirmName()" (input)="confirmName.set($any($event.target).value)" [placeholder]="d.name" />
        }
      </ng-container>
    </nz-modal>

    <nz-modal
      [nzVisible]="addingAccount()"
      nzTitle="เพิ่มบัญชี"
      nzOkText="เพิ่ม"
      [nzOkDisabled]="newAccountForm.invalid"
      [nzOkLoading]="savingAccount()"
      (nzOnOk)="addAccount()"
      (nzOnCancel)="addingAccount.set(false)"
    >
      <ng-container *nzModalContent>
        <form nz-form nzLayout="vertical" [formGroup]="newAccountForm" (ngSubmit)="addAccount()">
          <nz-form-item>
            <nz-form-label nzRequired>หมวด</nz-form-label>
            <nz-form-control nzExtra="ค่าใช้จ่าย/รายได้ ไปอยู่ในงบกำไรขาดทุน · สินทรัพย์/หนี้สิน/ทุน ไปอยู่ในงบดุล">
              <nz-select formControlName="type">
                @for (t of types; track t[0]) {
                  <nz-option [nzValue]="t[0]" [nzLabel]="t[1]"></nz-option>
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzRequired>รหัส</nz-form-label>
            <nz-form-control nzExtra="ขึ้นต้นตามหมวด: 1 สินทรัพย์ · 2 หนี้สิน · 3 ทุน · 4 รายได้ · 5 ค่าใช้จ่าย (เติมเลขถัดไปที่ว่างให้แล้ว)">
              <input nz-input class="num" formControlName="code" inputmode="numeric" maxlength="20" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item class="mb-0">
            <nz-form-label nzRequired>ชื่อบัญชี</nz-form-label>
            <nz-form-control>
              <input nz-input formControlName="name" placeholder="เช่น ค่าอินเทอร์เน็ต" maxlength="200" />
            </nz-form-control>
          </nz-form-item>
          <!-- Enter in a field submits -->
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .danger {
      color: var(--error);
    }
    .item {
      display: grid;
      gap: 2px;
      width: 100%;
      padding: 8px 12px;
      border: 0;
      border-radius: 6px;
      background: transparent;
      text-align: left;
      line-height: 1.4;
      cursor: pointer;
      transition: background 0.15s;
    }
    .item:hover { background: var(--surface-2); }
    .item.selected { background: var(--accent-soft); }
    .item.selected .name { color: var(--accent); }
    .name { font-size: 14px; font-weight: 500; color: var(--ink); }
    .meta { font-size: 12px; color: var(--ink-3); }
    .explain { font-size: 13px; }
    .inactive td { color: var(--ink-3); }
    .row-actions { white-space: nowrap; }
    .delete-slot { width: 28px; flex: none; }
  `,
})
export class Companies {
  private readonly api = inject(ReceiptApi);
  private readonly fb = inject(FormBuilder);
  private readonly message = inject(NzMessageService);
  private readonly confirm = injectConfirm();
  protected readonly store = inject(CompanyStore);
  /** Adding and removing companies is for super admins; admins edit the companies they run. */
  protected readonly auth = inject(Auth);

  protected readonly typeLabel = ACCOUNT_TYPE_LABEL;
  protected readonly roleLabel = ACCOUNT_ROLE_LABEL;
  protected readonly types = Object.entries(ACCOUNT_TYPE_LABEL) as [AccountType, string][];

  protected readonly creating = signal(false);
  private readonly editingId = signal<string | null>(null);
  protected readonly editing = computed(
    () => this.store.companies().find((c) => c.id === (this.editingId() ?? this.store.current()?.id)) ?? null,
  );
  protected readonly accounts = signal<Account[]>([]);
  protected readonly editingAccount = signal<string | null>(null);
  protected readonly busy = signal(false);
  /** Company waiting for delete confirmation (typed name must match). */
  protected readonly deleting = signal<Company | null>(null);
  protected readonly confirmName = signal('');

  protected readonly form = this.fb.nonNullable.group({
    name: '',
    taxId: '',
    branch: '00000',
    address: '',
    vatRegistered: true,
  });

  protected readonly accountForm = this.fb.nonNullable.group({
    code: '',
    name: '',
    type: 'Expense' as AccountType,
    active: true,
  });

  protected readonly newAccountForm = this.fb.nonNullable.group({
    code: ['', Validators.required],
    name: ['', Validators.required],
    type: 'Expense' as AccountType,
  });
  protected readonly addingAccount = signal(false);
  protected readonly savingAccount = signal(false);

  constructor() {
    this.newAccountForm.controls.type.valueChanges.pipe(takeUntilDestroyed()).subscribe((type) => {
      const code = this.newAccountForm.controls.code;
      if (code.pristine) code.setValue(this.nextCode(type));
    });

    effect(() => {
      const c = this.editing();
      untracked(() => {
        if (this.creating()) return;
        if (c) {
          this.fill(c);
          this.loadAccounts(c.id);
        } else if (this.store.loaded() && this.store.companies().length === 0) {
          this.startNew();
        }
      });
    });
  }

  protected edit(c: Company) {
    this.creating.set(false);
    this.editingId.set(c.id);
    this.fill(c);
    this.loadAccounts(c.id);
  }

  protected startNew() {
    this.creating.set(true);
    this.form.reset({ name: '', taxId: '', branch: '00000', address: '', vatRegistered: true });
  }

  protected cancelNew() {
    this.creating.set(false);
    const c = this.editing();
    if (c) this.fill(c);
  }

  protected saveCompany() {
    if (!this.auth.isSuperAdmin()) return;
    const v = this.form.getRawValue();
    const body = {
      name: v.name.trim(),
      taxId: v.taxId.trim() || null,
      branch: v.branch.trim() || null,
      address: v.address.trim() || null,
      vatRegistered: v.vatRegistered,
    };
    const target = this.editing();
    const req = this.creating() || !target ? this.api.createCompany(body) : this.api.updateCompany(target.id, body);
    this.busy.set(true);
    req.subscribe({
      next: (c) => {
        this.busy.set(false);
        const wasNew = this.creating();
        this.creating.set(false);
        this.editingId.set(c.id);
        // A new company becomes the one being worked on.
        this.store.reload(wasNew ? c.id : undefined);
        this.message.success('บันทึกแล้ว');
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected editAccount(a: Account) {
    this.editingAccount.set(a.id);
    this.accountForm.reset({ code: a.code, name: a.name, type: a.type, active: a.active });
    // System accounts keep their type and stay active: the journal generator posts to them.
    if (a.role !== 'None') {
      this.accountForm.controls.type.disable();
      this.accountForm.controls.active.disable();
    } else {
      this.accountForm.controls.type.enable();
      this.accountForm.controls.active.enable();
    }
  }

  protected saveAccount(a: Account) {
    const c = this.editing();
    if (!c) return;
    this.api.updateAccount(c.id, a.id, this.accountForm.getRawValue()).subscribe({
      next: () => {
        this.editingAccount.set(null);
        this.loadAccounts(c.id);
      },
      error: (e) => this.message.error(errorMessage(e)),
    });
  }

  protected openAddAccount() {
    const type = this.newAccountForm.controls.type.value;
    this.newAccountForm.reset({ code: this.nextCode(type), name: '', type });
    this.addingAccount.set(true);
  }

  /** The next free code in the type's range (1 assets … 5 expenses), e.g. 5221 after 5220. */
  private nextCode(type: AccountType): string {
    const prefix = ACCOUNT_CODE_PREFIX[type];
    const used = this.accounts()
      .map((a) => a.code)
      .filter((c) => /^\d+$/.test(c) && c.startsWith(prefix))
      .map(Number);
    return used.length ? String(Math.max(...used) + 1) : `${prefix}100`;
  }

  protected addAccount() {
    const c = this.editing();
    if (!c || this.newAccountForm.invalid || this.savingAccount()) return;
    const v = this.newAccountForm.getRawValue();
    this.savingAccount.set(true);
    this.api.createAccount(c.id, { ...v, active: true }).subscribe({
      next: () => {
        this.savingAccount.set(false);
        this.addingAccount.set(false);
        this.message.success(`เพิ่มบัญชี ${v.code.trim()} ${v.name.trim()} แล้ว`);
        this.loadAccounts(c.id);
      },
      error: (e) => {
        this.savingAccount.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected askDeleteAccount(c: Company, a: Account) {
    this.confirm({
      title: `ลบบัญชี ${a.code} ${a.name} ?`,
      content: 'บัญชีนี้ยังไม่เคยถูกใช้ ลบแล้วกู้คืนไม่ได้',
      okText: 'ลบ',
      danger: true,
      onOk: () => this.deleteAccount(c, a),
    });
  }

  protected deleteAccount(c: Company, a: Account) {
    this.api.deleteAccount(c.id, a.id).subscribe({
      next: () => {
        this.message.success(`ลบบัญชี ${a.code} ${a.name} แล้ว`);
        this.loadAccounts(c.id);
      },
      error: (e) => this.message.error(errorMessage(e)),
    });
  }

  protected askDelete(c: Company) {
    this.confirmName.set('');
    this.deleting.set(c);
  }

  protected deleteCompany() {
    const c = this.deleting();
    if (!c || this.confirmName() !== c.name) return;
    this.busy.set(true);
    this.api.deleteCompany(c.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.deleting.set(null);
        this.editingId.set(null);
        this.store.reload();
        this.message.success(`ลบ ${c.name} แล้ว`);
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  private fill(c: Company) {
    this.form.reset({
      name: c.name,
      taxId: c.taxId,
      branch: c.branch ?? '',
      address: c.address ?? '',
      vatRegistered: c.vatRegistered,
    });
    if (this.auth.isSuperAdmin()) this.form.enable();
    else this.form.disable();
  }

  private loadAccounts(companyId: string) {
    this.api.accounts(companyId).subscribe((a) => {
      this.accounts.set(a);
      if (companyId === this.store.current()?.id) this.store.accounts.set(a);
    });
  }
}
