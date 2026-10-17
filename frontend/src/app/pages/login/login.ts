import { Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { Auth, safeReturnUrl } from '../../core/auth';
import { errorMessage } from '../../core/receipt-api';

/** Sign-in page; with mode "setup" it creates the first admin on a fresh install instead. */
@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    NzAlertModule,
    NzButtonModule,
    NzCardModule,
    NzCheckboxModule,
    NzFormModule,
    NzIconModule,
    NzInputModule,
  ],
  template: `
    <div class="grid min-h-screen place-items-center px-4 py-10">
      <div class="w-full max-w-100">
        <div class="mb-6 text-center">
          <div class="brand">Receipt Reader</div>
          <p class="mt-1 mb-0 text-black/45">
            {{ setup() ? 'ตั้งค่าครั้งแรก — สร้างบัญชีผู้ดูแลระบบ' : 'เข้าสู่ระบบเพื่อใช้งาน' }}
          </p>
        </div>

        <nz-card>
          @if (setup()) {
            <p class="mt-0 mb-4 text-black/65">
              ยังไม่มีผู้ใช้ในระบบ บัญชีนี้จะเป็น<b>ผู้ดูแลระบบ</b> ซึ่งเพิ่มผู้ใช้คนอื่นได้ภายหลัง
            </p>
          }

          @if (error(); as e) {
            <nz-alert class="mb-4 block" nzType="error" [nzMessage]="e" nzShowIcon />
          }

          <form nz-form nzLayout="vertical" [formGroup]="form" (ngSubmit)="submit()">
            <nz-form-item>
              <nz-form-label nzFor="name" nzRequired>ชื่อผู้ใช้งาน</nz-form-label>
              <nz-form-control nzErrorTip="กรุณากรอกชื่อผู้ใช้งาน">
                <nz-input-wrapper>
                  <nz-icon nzInputPrefix nzType="user" />
                  <input
                    nz-input
                    id="name"
                    formControlName="name"
                    autocomplete="username"
                    autocapitalize="none"
                    spellcheck="false"
                    [attr.placeholder]="setup() ? 'เช่น สมชาย' : null"
                  />
                </nz-input-wrapper>
              </nz-form-control>
            </nz-form-item>

            <nz-form-item>
              <nz-form-label nzFor="password" nzRequired>รหัสผ่าน</nz-form-label>
              <nz-form-control
                [nzErrorTip]="setup() ? 'อย่างน้อย 8 ตัวอักษร' : 'กรุณากรอกรหัสผ่าน'"
              >
                <nz-input-wrapper>
                  <nz-icon nzInputPrefix nzType="lock" />
                  <input
                    nz-input
                    id="password"
                    type="password"
                    formControlName="password"
                    [attr.autocomplete]="setup() ? 'new-password' : 'current-password'"
                  />
                </nz-input-wrapper>
              </nz-form-control>
            </nz-form-item>

            @if (setup()) {
              <nz-form-item>
                <nz-form-label nzFor="confirm" nzRequired>ยืนยันรหัสผ่าน</nz-form-label>
                <nz-form-control
                  [nzValidateStatus]="mismatch() ? 'error' : ''"
                  nzErrorTip="รหัสผ่านไม่ตรงกัน"
                >
                  <nz-input-wrapper>
                    <nz-icon nzInputPrefix nzType="lock" />
                    <input
                      nz-input
                      id="confirm"
                      type="password"
                      formControlName="confirm"
                      autocomplete="new-password"
                    />
                  </nz-input-wrapper>
                </nz-form-control>
              </nz-form-item>
            } @else {
              <nz-form-item>
                <label nz-checkbox formControlName="remember">จดจำการเข้าสู่ระบบ 7 วัน</label>
              </nz-form-item>
            }

            <button
              nz-button
              nzType="primary"
              nzBlock
              nzSize="large"
              type="submit"
              [nzLoading]="busy()"
            >
              {{ setup() ? 'สร้างบัญชีและเข้าสู่ระบบ' : 'เข้าสู่ระบบ' }}
            </button>
          </form>
        </nz-card>

        @if (!setup()) {
          <p class="mt-4 text-center text-[13px] text-black/45">
            ลืมรหัสผ่าน? ติดต่อผู้ดูแลระบบให้ตั้งรหัสใหม่
          </p>
        }
      </div>
    </div>
  `,
  styles: `
    .brand {
      color: var(--ink);
      font-size: 22px;
      font-weight: 600;
    }
  `,
})
export class Login {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  /** Route data: "login" or "setup". */
  readonly mode = input<'login' | 'setup'>('login');
  /** ?returnUrl= — where to go after signing in. */
  readonly returnUrl = input<string | undefined>();

  protected readonly setup = computed(() => this.mode() === 'setup');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly submitted = signal(false);

  /** The password rule depends on the mode, so it is attached in submit(). */
  protected readonly form = this.fb.nonNullable.group({
    name: ['', Validators.required],
    password: [''],
    confirm: [''],
    remember: [false],
  });

  protected readonly mismatch = () =>
    this.setup() &&
    this.submitted() &&
    this.form.controls.confirm.value !== this.form.controls.password.value;

  submit() {
    this.submitted.set(true);
    const c = this.form.controls;
    c.password.setValidators(
      this.setup() ? [Validators.required, Validators.minLength(8)] : Validators.required,
    );
    for (const ctrl of Object.values(c)) ctrl.updateValueAndValidity();
    if (this.form.invalid || this.mismatch()) {
      for (const ctrl of Object.values(c)) ctrl.markAsDirty();
      return;
    }

    const v = this.form.getRawValue();
    const request = this.setup()
      ? this.auth.setup(v.name, v.password)
      : this.auth.login(v.name, v.password, v.remember);

    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => void this.router.navigateByUrl(safeReturnUrl(this.returnUrl())),
      error: (err) => {
        this.busy.set(false);
        this.error.set(errorMessage(err));
        this.form.controls.password.reset('');
      },
    });
  }
}
