import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzDrawerModule } from 'ng-zorro-antd/drawer';
import { NzDropDownModule } from 'ng-zorro-antd/dropdown';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzLayoutModule } from 'ng-zorro-antd/layout';
import { NzMenuModule } from 'ng-zorro-antd/menu';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { filter, map } from 'rxjs';
import { Auth } from './core/auth';
import { CompanyStore } from './core/company-store';
import { USER_ROLE_LABEL } from './core/models';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    FormsModule,
    NzLayoutModule,
    NzMenuModule,
    NzSelectModule,
    NzAlertModule,
    NzButtonModule,
    NzIconModule,
    NzDrawerModule,
    NzDropDownModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);
  protected readonly auth = inject(Auth);
  private readonly userId = computed(() => this.auth.user()?.id ?? null);
  protected readonly store = inject(CompanyStore);
  /** Side drawer that replaces the top menu on narrow screens. */
  protected readonly navOpen = signal(false);

  /** Users (ผู้บันทึกบัญชี) only record documents; the accounting overview is for approvers and up. */
  protected readonly mainLinks = computed(() => [
    { path: '/receipts', label: 'เอกสาร', icon: 'file-text' },
    ...(this.auth.canApprove()
      ? [
          { path: '/outstanding', label: 'ค้างชำระ', icon: 'clock-circle' },
          { path: '/periods', label: 'งวดบัญชี', icon: 'calendar' },
          { path: '/journal', label: 'สมุดรายวัน', icon: 'book' },
        ]
      : []),
  ]);
  /** Stock pages under one "สต็อก" menu; every role reads them, admins change them. */
  protected readonly stockLinks = [
    { path: '/products', label: 'สินค้า', icon: 'appstore' },
    { path: '/warehouses', label: 'คลังสินค้า', icon: 'database' },
  ];
  /** Management areas under one "จัดการระบบ" menu; empty for approvers and users. */
  protected readonly adminLinks = computed(() => [
    ...(this.auth.isManager()
      ? [
          { path: '/companies', label: 'จัดการบริษัท', icon: 'bank' },
          { path: '/users', label: 'จัดการผู้ใช้งาน', icon: 'team' },
        ]
      : []),
    ...(this.auth.isSuperAdmin() ? [{ path: '/ai', label: 'Training AI', icon: 'robot' }] : []),
  ]);
  protected readonly roleLabel = USER_ROLE_LABEL;

  protected readonly onCompaniesPage = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects.startsWith('/companies')),
    ),
    { initialValue: false },
  );

  constructor() {
    // Company data is only available to a signed-in user; reload it when the user changes.
    effect(() => {
      const userId = this.userId();
      untracked(() => {
        if (!userId) {
          this.store.clear();
          return;
        }
        this.store.reload();
      });
    });
  }

  logout() {
    this.navOpen.set(false);
    this.auth.logout().subscribe({
      complete: () => void this.router.navigate(['/login']),
      error: () => void this.router.navigate(['/login']),
    });
  }
}
