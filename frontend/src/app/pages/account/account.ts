import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzDescriptionsModule } from 'ng-zorro-antd/descriptions';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { Auth } from '../../core/auth';
import { USER_ROLE_COLOR, USER_ROLE_LABEL } from '../../core/models';
import { errorMessage } from '../../core/receipt-api';

@Component({
  selector: 'app-account',
  imports: [
    ReactiveFormsModule,
    NzButtonModule,
    NzCardModule,
    NzDescriptionsModule,
    NzFormModule,
    NzIconModule,
    NzInputModule,
    NzTagModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-180 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header>
        <h1 class="m-0 text-[20px] font-semibold">บัญชีของฉัน</h1>
      </header>

      @if (auth.user(); as me) {
        <nz-card nzTitle="ข้อมูลผู้ใช้">
          <nz-descriptions [nzColumn]="1" nzSize="small">
            <nz-descriptions-item nzTitle="ชื่อผู้ใช้งาน">{{ me.name }}</nz-descriptions-item>
            <nz-descriptions-item nzTitle="สิทธิ์">
              <nz-tag [nzColor]="roleColor[me.role]">{{
                roleLabel[me.role]
              }}</nz-tag>
            </nz-descriptions-item>
          </nz-descriptions>
        </nz-card>
      }

      <nz-card nzTitle="เปลี่ยนรหัสผ่าน">
        <p class="mt-0 text-black/45">
          เปลี่ยนแล้ว อุปกรณ์อื่นที่ล็อกอินบัญชีนี้ไว้จะถูกออกจากระบบ
        </p>
        <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="submit()" class="max-w-90">
          <nz-form-item>
            <nz-form-label nzFor="current" nzRequired>รหัสผ่านปัจจุบัน</nz-form-label>
            <nz-form-control nzErrorTip="กรุณากรอกรหัสผ่านปัจจุบัน">
              <input
                nz-input
                id="current"
                type="password"
                formControlName="current"
                autocomplete="current-password"
              />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzFor="next" nzRequired>รหัสผ่านใหม่</nz-form-label>
            <nz-form-control nzErrorTip="อย่างน้อย 8 ตัวอักษร">
              <input
                nz-input
                id="next"
                type="password"
                formControlName="next"
                autocomplete="new-password"
              />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzFor="confirm" nzRequired>ยืนยันรหัสผ่านใหม่</nz-form-label>
            <nz-form-control
              [nzValidateStatus]="mismatch() ? 'error' : ''"
              nzErrorTip="รหัสผ่านไม่ตรงกัน"
            >
              <input
                nz-input
                id="confirm"
                type="password"
                formControlName="confirm"
                autocomplete="new-password"
              />
            </nz-form-control>
          </nz-form-item>
          <button nz-button nzType="primary" type="submit" [nzLoading]="busy()">
            <nz-icon nzType="key" /> เปลี่ยนรหัสผ่าน
          </button>
        </form>
      </nz-card>
    </div>
  `,
})
export class Account {
  protected readonly auth = inject(Auth);
  private readonly fb = inject(FormBuilder);
  private readonly msg = inject(NzMessageService);

  protected readonly roleLabel = USER_ROLE_LABEL;
  protected readonly roleColor = USER_ROLE_COLOR;
  protected readonly busy = signal(false);
  private readonly submitted = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    current: ['', Validators.required],
    next: ['', [Validators.required, Validators.minLength(8)]],
    confirm: [''],
  });

  protected readonly mismatch = () =>
    this.submitted() && this.form.controls.confirm.value !== this.form.controls.next.value;

  submit() {
    this.submitted.set(true);
    if (this.form.invalid || this.mismatch()) {
      for (const ctrl of Object.values(this.form.controls)) {
        ctrl.markAsDirty();
        ctrl.updateValueAndValidity();
      }
      return;
    }
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.auth.changePassword(v.current, v.next).subscribe({
      next: () => {
        this.busy.set(false);
        this.submitted.set(false);
        this.form.reset();
        this.msg.success('เปลี่ยนรหัสผ่านแล้ว');
      },
      error: (err) => {
        this.busy.set(false);
        this.msg.error(errorMessage(err));
      },
    });
  }
}
