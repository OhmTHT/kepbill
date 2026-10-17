import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { AuthStatus, User } from './models';

/** Who is signed in. The session itself is an HttpOnly cookie the browser sends with every /api request. */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/auth';
  private status$: Observable<AuthStatus> | null = null;

  readonly user = signal<User | null>(null);
  readonly setupRequired = signal(false);
  private readonly role = computed(() => this.user()?.role);
  /** Every company and every feature, including adding companies and Training AI. */
  readonly isSuperAdmin = computed(() => this.role() === 'SuperAdmin');
  /** Company settings and users: admins (their companies) and super admins. */
  readonly isManager = computed(() => this.role() === 'SuperAdmin' || this.role() === 'Admin');
  /** Approving, payments, periods, journal, outstanding: everyone but plain users. */
  readonly canApprove = computed(() => !!this.role() && this.role() !== 'User');

  /** Asks the API who is signed in, once; guards wait on this before deciding. */
  ensureLoaded(): Observable<AuthStatus> {
    this.status$ ??= this.http.get<AuthStatus>(`${this.base}/status`).pipe(
      tap((s) => this.apply(s)),
      shareReplay(1),
    );
    return this.status$;
  }

  /** Signs in with ชื่อผู้ใช้งาน and password. */
  login(name: string, password: string, remember: boolean) {
    return this.http
      .post<AuthStatus>(`${this.base}/login`, { name, password, remember })
      .pipe(tap((s) => this.apply(s)));
  }

  /** Creates the first admin account (only possible while there are no users) and signs in. */
  setup(name: string, password: string) {
    return this.http
      .post<AuthStatus>(`${this.base}/setup`, { name, password })
      .pipe(tap((s) => this.apply(s)));
  }

  logout() {
    return this.http.post<void>(`${this.base}/logout`, {}).pipe(finalize(() => this.signedOut()));
  }

  changePassword(currentPassword: string, newPassword: string) {
    return this.http.post<void>(`${this.base}/change-password`, { currentPassword, newPassword });
  }

  /** The session is gone (logout, expiry, or deactivated elsewhere). */
  signedOut() {
    this.apply({ setupRequired: this.setupRequired(), user: null });
  }

  private apply(s: AuthStatus) {
    this.user.set(s.user);
    this.setupRequired.set(s.setupRequired);
    this.status$ = of(s);
  }
}

/** Pages behind the login. Sends visitors to /setup on a fresh install, otherwise to /login. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(Auth);
  const router = inject(Router);
  return auth.ensureLoaded().pipe(
    map(() => {
      if (auth.user()) return true;
      if (auth.setupRequired()) return router.createUrlTree(['/setup']);
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }),
    catchError(() => of(router.createUrlTree(['/login']))),
  );
};

/** A page for some roles only; everyone else lands on the document list. */
function roleGuard(allowed: (auth: Auth) => boolean): CanActivateFn {
  return () => {
    const auth = inject(Auth);
    const router = inject(Router);
    return auth.ensureLoaded().pipe(map(() => allowed(auth) || router.createUrlTree(['/receipts'])));
  };
}

export const superAdminGuard = roleGuard((a) => a.isSuperAdmin());
export const managerGuard = roleGuard((a) => a.isManager());
export const approverGuard = roleGuard((a) => a.canApprove());

/** /login and /setup: only for visitors who are not signed in; /setup only on a fresh install. */
export function guestGuard(page: 'login' | 'setup'): CanActivateFn {
  return () => {
    const auth = inject(Auth);
    const router = inject(Router);
    return auth.ensureLoaded().pipe(
      map(() => {
        if (auth.user()) return router.createUrlTree(['/receipts']);
        if (page === 'login' && auth.setupRequired()) return router.createUrlTree(['/setup']);
        if (page === 'setup' && !auth.setupRequired()) return router.createUrlTree(['/login']);
        return true;
      }),
      catchError(() => of(true)),
    );
  };
}

/** A 401 from the API means the session ended: drop the user and go to the login page. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const router = inject(Router);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        !req.url.startsWith('/api/auth/') &&
        auth.user()
      ) {
        auth.signedOut();
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => err);
    }),
  );
};

/** Only same-app paths, so a crafted ?returnUrl= cannot send the user to another site. */
export function safeReturnUrl(url: string | null | undefined): string {
  return url &&
    url.startsWith('/') &&
    !url.startsWith('//') &&
    !url.startsWith('/\\') &&
    !url.startsWith('/login') &&
    !url.startsWith('/setup')
    ? url
    : '/receipts';
}
