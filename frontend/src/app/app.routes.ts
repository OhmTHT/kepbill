import { Routes } from '@angular/router';
import { approverGuard, authGuard, guestGuard, managerGuard, superAdminGuard } from './core/auth';

export const routes: Routes = [
  {
    path: 'login',
    title: 'เข้าสู่ระบบ · Receipt Reader',
    canActivate: [guestGuard('login')],
    data: { mode: 'login' },
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
  },
  {
    path: 'setup',
    title: 'ตั้งค่าครั้งแรก · Receipt Reader',
    canActivate: [guestGuard('setup')],
    data: { mode: 'setup' },
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
  },
  {
    // Everything else requires a signed-in user.
    path: '',
    canActivateChild: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'receipts' },
      {
        path: 'receipts',
        title: 'เอกสาร · Receipt Reader',
        loadComponent: () => import('./pages/inbox/inbox').then((m) => m.Inbox),
      },
      {
        path: 'receipts/:id',
        title: 'ตรวจเอกสาร · Receipt Reader',
        loadComponent: () => import('./pages/review/review').then((m) => m.Review),
      },
      {
        path: 'products',
        title: 'สินค้า · Receipt Reader',
        loadComponent: () => import('./pages/products/products').then((m) => m.Products),
      },
      {
        path: 'warehouses',
        title: 'คลังสินค้า · Receipt Reader',
        loadComponent: () => import('./pages/warehouses/warehouses').then((m) => m.Warehouses),
      },
      {
        path: 'outstanding',
        title: 'ค้างชำระ · Receipt Reader',
        canActivate: [approverGuard],
        loadComponent: () =>
          import('./pages/outstanding/outstanding').then((m) => m.OutstandingPage),
      },
      {
        path: 'periods',
        title: 'งวดบัญชี · Receipt Reader',
        canActivate: [approverGuard],
        loadComponent: () => import('./pages/periods/periods').then((m) => m.Periods),
      },
      {
        path: 'journal',
        title: 'สมุดรายวัน · Receipt Reader',
        canActivate: [approverGuard],
        loadComponent: () => import('./pages/journal/journal').then((m) => m.JournalPage),
      },
      {
        path: 'ai',
        title: 'Training AI · Receipt Reader',
        canActivate: [superAdminGuard],
        loadComponent: () => import('./pages/ai/ai').then((m) => m.AiPage),
      },
      {
        path: 'companies',
        title: 'บริษัท · Receipt Reader',
        canActivate: [managerGuard],
        loadComponent: () => import('./pages/companies/companies').then((m) => m.Companies),
      },
      {
        path: 'account',
        title: 'บัญชีของฉัน · Receipt Reader',
        loadComponent: () => import('./pages/account/account').then((m) => m.Account),
      },
      {
        path: 'users',
        title: 'ผู้ใช้ · Receipt Reader',
        canActivate: [managerGuard],
        loadComponent: () => import('./pages/users/users').then((m) => m.Users),
      },
      { path: 'vendors', redirectTo: 'ai' },
    ],
  },
  { path: '**', redirectTo: 'receipts' },
];
