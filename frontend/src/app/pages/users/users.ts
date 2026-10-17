import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Auth } from '../../core/auth';
import { CompanyStore } from '../../core/company-store';
import {
  USER_ROLE_COLOR,
  USER_ROLE_HINT,
  USER_ROLE_LABEL,
  User,
  UserRole,
  assignableRoles,
} from '../../core/models';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';
import { ActiveMark } from '../../core/active-mark';

/** Admins: add people, choose which companies a member may open, change roles, deactivate, set a new password. */
@Component({
  selector: 'app-users',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    NzAlertModule,
    ActiveMark,
    NzButtonModule,
    NzCardModule,
    NzFormModule,
    NzIconModule,
    NzInputModule,
    NzModalModule,
    NzSelectModule,
    NzSwitchModule,
    NzTableModule,
    NzTagModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">ผู้ใช้</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            @if (auth.isSuperAdmin()) {
              ผู้ดูแลระบบเห็นทุกบริษัท · บทบาทอื่นเห็นเฉพาะบริษัทที่ผูกไว้ ·
            } @else {
              จัดการได้เฉพาะผู้ทำบัญชีและผู้บันทึกบัญชีในบริษัทที่คุณดูแล ·
            }
            ปิดบัญชีแล้วผู้ใช้คนนั้นจะถูกออกจากระบบทันที
          </p>
        </div>
        <button nz-button nzType="primary" (click)="openCreate()">
          <nz-icon nzType="plus" /> เพิ่มผู้ใช้
        </button>
      </header>

      <nz-card [nzBodyStyle]="{ padding: '0' }">
        <nz-table
          #t
          nzSize="small"
          [nzData]="users()"
          [nzLoading]="loading()"
          [nzFrontPagination]="false"
          [nzShowPagination]="false"
          [nzScroll]="{ x: '860px' }"
        >
          <thead>
            <tr>
              <th nzWidth="180px">ชื่อผู้ใช้งาน</th>
              <th nzWidth="120px">สิทธิ์</th>
              <th>บริษัทที่เข้าถึงได้</th>
              <th nzWidth="64px" nzAlign="center">สถานะ</th>
              <th nzWidth="140px">เข้าสู่ระบบล่าสุด</th>
              <th nzWidth="190px"></th>
            </tr>
          </thead>
          <tbody>
            @for (u of t.data; track u.id) {
              <tr [class.inactive]="!u.active">
                <td>
                  {{ u.name }}
                  @if (u.id === auth.user()?.id) {
                    <span class="muted">(คุณ)</span>
                  }
                </td>
                <td>
                  <nz-tag [nzColor]="roleColor[u.role]">{{
                    roleLabel[u.role]
                  }}</nz-tag>
                </td>
                <td>
                  @if (u.role === 'SuperAdmin') {
                    <span class="muted">ทุกบริษัท</span>
                  } @else if (u.companyIds.length === 0) {
                    <span class="warn">ยังไม่ได้ผูกบริษัท</span>
                  } @else {
                    @for (name of companyNames(u); track name) {
                      <nz-tag class="mb-0.5">{{ name }}</nz-tag>
                    }
                  }
                </td>
                <td nzAlign="center"><app-active-mark [active]="u.active" /></td>
                <td class="muted">
                  {{ u.lastLoginAt ? (u.lastLoginAt | date: 'd MMM yy HH:mm') : '—' }}
                </td>
                <td nzAlign="right" class="whitespace-nowrap">
                  <button nz-button nzType="text" nzSize="small" (click)="openEdit(u)">
                    <nz-icon nzType="edit" /> แก้ไข
                  </button>
                  <button nz-button nzType="text" nzSize="small" (click)="openReset(u)">
                    <nz-icon nzType="key" /> ตั้งรหัสใหม่
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </nz-table>
      </nz-card>
    </div>

    <!-- add / edit -->
    <nz-modal
      [nzVisible]="editing() !== undefined"
      [nzTitle]="editing() ? 'แก้ไขผู้ใช้' : 'เพิ่มผู้ใช้'"
      nzOkText="บันทึก"
      [nzOkLoading]="busy()"
      (nzOnOk)="save()"
      (nzOnCancel)="editing.set(undefined)"
    >
      <ng-container *nzModalContent>
        <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="save()">
          <nz-form-item>
            <nz-form-label nzRequired>ชื่อผู้ใช้งาน</nz-form-label>
            <nz-form-control
              nzErrorTip="กรุณากรอกชื่อผู้ใช้งาน"
              nzExtra="ใช้เข้าสู่ระบบ ห้ามซ้ำกับผู้ใช้คนอื่น"
            >
              <input nz-input formControlName="name" autocomplete="off" />
            </nz-form-control>
          </nz-form-item>
          @if (!editing()) {
            <nz-form-item>
              <nz-form-label nzRequired>รหัสผ่านเริ่มต้น</nz-form-label>
              <nz-form-control
                nzErrorTip="อย่างน้อย 8 ตัวอักษร"
                nzExtra="ส่งให้ผู้ใช้ทางช่องทางที่ปลอดภัย แล้วให้เปลี่ยนเองในหน้า “บัญชีของฉัน”"
              >
                <input
                  nz-input
                  type="text"
                  formControlName="password"
                  autocomplete="new-password"
                />
              </nz-form-control>
            </nz-form-item>
          }
          <nz-form-item>
            <nz-form-label>บทบาท</nz-form-label>
            <nz-form-control [nzExtra]="roleHint[form.controls.role.value]">
              <nz-select formControlName="role">
                @for (r of roles(); track r) {
                  <nz-option [nzValue]="r" [nzLabel]="roleLabel[r]"></nz-option>
                }
              </nz-select>
            </nz-form-control>
          </nz-form-item>
          @if (form.controls.role.value !== 'SuperAdmin') {
            <nz-form-item>
              <nz-form-label>บริษัทที่เข้าถึงได้</nz-form-label>
              <nz-form-control
                nzExtra="เลือกได้หลายบริษัท — ผู้ใช้คนนี้จะเห็นและทำงานได้เฉพาะบริษัทที่เลือก"
              >
                <nz-select
                  formControlName="companyIds"
                  nzMode="multiple"
                  nzShowSearch
                  nzAllowClear
                  nzPlaceHolder="เลือกบริษัท"
                >
                  @for (c of store.companies(); track c.id) {
                    <nz-option [nzValue]="c.id" [nzLabel]="c.name"></nz-option>
                  }
                </nz-select>
              </nz-form-control>
            </nz-form-item>
            @if (form.controls.companyIds.value.length === 0) {
              <nz-alert
                class="mb-4 block"
                nzType="warning"
                nzShowIcon
                nzMessage="ยังไม่ได้เลือกบริษัท — ผู้ใช้คนนี้จะเข้าระบบได้แต่ไม่เห็นข้อมูลใดๆ"
              />
            }
          } @else {
            <p class="muted mt-0">ผู้ดูแลระบบเห็นทุกบริษัทโดยอัตโนมัติ</p>
          }
          @if (editing()) {
            <nz-form-item>
              <nz-form-label>เปิดใช้งาน</nz-form-label>
              <nz-form-control><nz-switch formControlName="active" /></nz-form-control>
            </nz-form-item>
          }
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>

    <!-- reset password -->
    <nz-modal
      [nzVisible]="resetting() !== null"
      [nzTitle]="'ตั้งรหัสผ่านใหม่ให้ ' + (resetting()?.name ?? '')"
      nzOkText="ตั้งรหัสผ่าน"
      [nzOkLoading]="busy()"
      (nzOnOk)="reset()"
      (nzOnCancel)="resetting.set(null)"
    >
      <ng-container *nzModalContent>
        <p class="mt-0 text-black/45">
          ผู้ใช้จะถูกออกจากระบบทุกอุปกรณ์ และบัญชีที่ถูกล็อกจะถูกปลดล็อก
        </p>
        <form nz-form nzLayout="vertical" (ngSubmit)="reset()">
          <nz-form-item>
            <nz-form-label nzRequired>รหัสผ่านใหม่</nz-form-label>
            <nz-form-control nzErrorTip="อย่างน้อย 8 ตัวอักษร">
              <input
                nz-input
                type="text"
                [formControl]="resetPassword"
                autocomplete="new-password"
              />
            </nz-form-control>
          </nz-form-item>
          <button type="submit" hidden></button>
        </form>
      </ng-container>
    </nz-modal>
  `,
  styles: `
    tr.inactive td {
      color: var(--ink-3);
    }
    .warn {
      color: var(--warn);
    }
  `,
})
export class Users {
  private readonly api = inject(ReceiptApi);
  private readonly fb = inject(FormBuilder);
  private readonly msg = inject(NzMessageService);
  protected readonly auth = inject(Auth);
  protected readonly store = inject(CompanyStore);

  protected readonly roleLabel = USER_ROLE_LABEL;
  protected readonly roleColor = USER_ROLE_COLOR;
  protected readonly roleHint = USER_ROLE_HINT;
  /** What the signed-in user may hand out: everything for super admins, approver/user for admins. */
  protected readonly roles = computed(() => assignableRoles(this.auth.user()?.role));
  protected readonly users = signal<User[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  /** undefined = closed, null = adding, a user = editing. */
  protected readonly editing = signal<User | null | undefined>(undefined);
  protected readonly resetting = signal<User | null>(null);

  private readonly companyName = computed(
    () => new Map(this.store.companies().map((c) => [c.id, c.name])),
  );

  protected readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    password: ['', [Validators.required, Validators.minLength(8)]],
    role: ['User' as UserRole],
    companyIds: [[] as string[]],
    active: [true],
  });
  protected readonly resetPassword = this.fb.nonNullable.control('', [
    Validators.required,
    Validators.minLength(8),
  ]);

  constructor() {
    this.load();
  }

  load() {
    this.api.users().subscribe({
      next: (list) => {
        this.users.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.msg.error(errorMessage(err));
      },
    });
  }

  protected companyNames(u: User): string[] {
    const names = this.companyName();
    return u.companyIds.map((id) => names.get(id) ?? '(บริษัทที่ถูกลบ)');
  }

  openCreate() {
    this.form.reset({ name: '', password: '', role: 'User', companyIds: [], active: true });
    this.form.controls.password.enable();
    this.editing.set(null);
  }

  openEdit(u: User) {
    this.form.reset({
      name: u.name,
      password: '',
      role: u.role,
      companyIds: [...u.companyIds],
      active: u.active,
    });
    this.form.controls.password.disable();
    this.editing.set(u);
  }

  save() {
    if (this.form.invalid) {
      for (const ctrl of Object.values(this.form.controls)) {
        ctrl.markAsDirty();
        ctrl.updateValueAndValidity();
      }
      return;
    }
    const v = this.form.getRawValue();
    const companyIds = v.role === 'SuperAdmin' ? [] : v.companyIds;
    const target = this.editing();
    const request = target
      ? this.api.updateUser(target.id, { name: v.name, role: v.role, active: v.active, companyIds })
      : this.api.createUser({ name: v.name, password: v.password, role: v.role, companyIds });

    this.busy.set(true);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.editing.set(undefined);
        this.msg.success(target ? 'บันทึกแล้ว' : `เพิ่มผู้ใช้ ${v.name} แล้ว`);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.msg.error(errorMessage(err));
      },
    });
  }

  openReset(u: User) {
    this.resetPassword.reset('');
    this.resetting.set(u);
  }

  reset() {
    const u = this.resetting();
    if (!u) return;
    if (this.resetPassword.invalid) {
      this.resetPassword.markAsDirty();
      this.resetPassword.updateValueAndValidity();
      return;
    }
    this.busy.set(true);
    this.api.resetPassword(u.id, this.resetPassword.value).subscribe({
      next: () => {
        this.busy.set(false);
        this.resetting.set(null);
        this.msg.success(`ตั้งรหัสผ่านใหม่ให้ ${u.name} แล้ว`);
      },
      error: (err) => {
        this.busy.set(false);
        this.msg.error(errorMessage(err));
      },
    });
  }
}
