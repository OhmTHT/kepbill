import { Injectable, computed, inject, signal } from '@angular/core';
import { Account, Company } from './models';
import { ReceiptApi } from './receipt-api';

const STORAGE_KEY = 'receipt-reader.companyId';

/** The companies, the one being worked on (remembered per browser), and its chart of accounts. */
@Injectable({ providedIn: 'root' })
export class CompanyStore {
  private readonly api = inject(ReceiptApi);

  readonly companies = signal<Company[]>([]);
  readonly loaded = signal(false);
  readonly currentId = signal<string | null>(readStored());
  readonly accounts = signal<Account[]>([]);

  readonly current = computed<Company | null>(() => {
    const list = this.companies();
    return list.find((c) => c.id === this.currentId()) ?? list[0] ?? null;
  });

  readonly activeAccounts = computed(() => this.accounts().filter((a) => a.active));
  /** What a purchase can be booked to: expenses, or an asset such as equipment (not cash, VAT or receivables). */
  readonly expenseAccounts = computed(() =>
    this.activeAccounts().filter(
      (a) => a.type === 'Expense' || (a.type === 'Asset' && a.role === 'None'),
    ),
  );
  readonly revenueAccounts = computed(() =>
    this.activeAccounts().filter((a) => a.type === 'Revenue'),
  );
  readonly moneyAccounts = computed(() =>
    this.activeAccounts().filter(
      (a) => a.role === 'Cash' || a.role === 'Bank' || (a.type === 'Asset' && a.role === 'None'),
    ),
  );

  /** Loaded by the app shell once someone signs in. */
  reload(selectId?: string) {
    this.api.companies().subscribe((list) => {
      this.companies.set(list);
      this.loaded.set(true);
      if (selectId) this.select(selectId);
      else if (this.current()) this.loadAccounts(this.current()!.id);
    });
  }

  /** Signed out: forget the previous user's data (the remembered company id stays for next time). */
  clear() {
    this.companies.set([]);
    this.accounts.set([]);
    this.loaded.set(false);
  }

  select(id: string) {
    this.currentId.set(id);
    try {
      localStorage.setItem(STORAGE_KEY, id);
    } catch {
      /* storage unavailable: the choice just isn't remembered */
    }
    this.loadAccounts(id);
  }

  loadAccounts(companyId = this.current()?.id) {
    if (!companyId) return;
    this.api.accounts(companyId).subscribe((a) => this.accounts.set(a));
  }
}

function readStored(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
