import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';

export interface DevelopmentIdentityProfile {
  key: string;
  userId: string;
  displayName: string;
  roles: string[];
  locationScopes: string[];
  competencyCodes: string[];
}

export interface CurrentIdentity {
  userId: string;
  displayName: string;
  roles: string[];
  locationScopes: string[];
  competencyCodes: string[];
  isDevelopmentIdentity: boolean;
}

const ROLE_LABELS: Readonly<Record<string, string>> = {
  Administrator: 'Administrator',
  Sponsor: 'Sponsor',
  HSEValidator: 'PIC HSE',
  AreaOwnerSeniorOfficer: 'SO / Officer Pemilik Wilayah',
  AreaOwnerManager: 'Manager Pemilik Wilayah',
};

export const OPERATION_BOARD_ROLES = [
  'Administrator',
  'HSEValidator',
  'AreaOwnerSeniorOfficer',
  'AreaOwnerManager',
] as const;

export function canAccessOperationsBoard(roles: readonly string[]): boolean {
  return roles.some((role) => (OPERATION_BOARD_ROLES as readonly string[]).includes(role));
}

export function canMonitorAllPermits(
  roles: readonly string[],
  locationScopes: readonly string[],
): boolean {
  return (
    locationScopes.includes('ORF') &&
    roles.some((role) => ['AreaOwnerSeniorOfficer', 'AreaOwnerManager'].includes(role))
  );
}

export function roleDisplayLabel(roles: readonly string[]): string {
  return roles.map((role) => ROLE_LABELS[role] ?? role).join(' · ');
}

export const DEVELOPMENT_IDENTITIES: DevelopmentIdentityProfile[] = [
  {
    key: 'sponsor-admin',
    userId: 'sponsor.demo',
    displayName: 'Sponsor Demo',
    roles: ['Sponsor', 'Administrator'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'admin-maker',
    userId: 'admin.maker.demo',
    displayName: 'Admin Maker Demo',
    roles: ['Administrator'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'admin-checker',
    userId: 'admin.checker.demo',
    displayName: 'Admin Checker Demo',
    roles: ['Administrator'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'sponsor-only',
    userId: 'sponsor.only.demo',
    displayName: 'Sponsor Only Demo',
    roles: ['Sponsor'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'hse-validator',
    userId: 'hse.validator.demo',
    displayName: 'PIC HSE Demo',
    roles: ['HSEValidator'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'area-senior-officer-site-office',
    userId: 'area.senior-officer.site-office.demo',
    displayName: 'Senior Officer Pemilik Wilayah Site-Office Demo',
    roles: ['AreaOwnerSeniorOfficer'],
    locationScopes: ['SITE_OFFICE'],
    competencyCodes: [],
  },
  {
    key: 'area-senior-officer-orf',
    userId: 'area.senior-officer.orf.demo',
    displayName: 'Senior Officer Distribusi Gas dan Manajemen ORF Demo',
    roles: ['AreaOwnerSeniorOfficer'],
    locationScopes: ['ORF'],
    competencyCodes: [],
  },
  {
    key: 'area-senior-officer-water-based',
    userId: 'area.senior-officer.water-based.demo',
    displayName: 'Senior Officer Pemilik Wilayah Water-Based Demo',
    roles: ['AreaOwnerSeniorOfficer'],
    locationScopes: ['WATER_BASED'],
    competencyCodes: [],
  },
  {
    key: 'area-owner-site-office',
    userId: 'area.owner.site-office.demo',
    displayName: 'Manager Pemilik Wilayah Site-Office (General Affair) Demo',
    roles: ['AreaOwnerManager'],
    locationScopes: ['SITE_OFFICE'],
    competencyCodes: [],
  },
  {
    key: 'area-owner-orf',
    userId: 'area.owner.orf.demo',
    displayName: 'Manager Pemilik Wilayah ORF Demo',
    roles: ['AreaOwnerManager'],
    locationScopes: ['ORF'],
    competencyCodes: [],
  },
  {
    key: 'area-owner-water-based',
    userId: 'area.owner.water-based.demo',
    displayName: 'Manager Pemilik Wilayah Water-Based (Transport & Operasi FSRU) Demo',
    roles: ['AreaOwnerManager'],
    locationScopes: ['WATER_BASED'],
    competencyCodes: [],
  },
];

const StorageKey = 'ptw.development-identity';
export const LocalLoginSessionKey = 'ptw.local-login';
export const DemoSessionKey = 'ptw.demo-session';

export function hasClientAuthMode(): boolean {
  if (typeof sessionStorage === 'undefined') return false;
  return (
    sessionStorage.getItem(LocalLoginSessionKey) === '1' ||
    sessionStorage.getItem(DemoSessionKey) === '1'
  );
}

export function startLocalLoginSession(): void {
  if (typeof sessionStorage === 'undefined') return;
  sessionStorage.setItem(LocalLoginSessionKey, '1');
  sessionStorage.removeItem(DemoSessionKey);
}

export function startDemoSession(): void {
  if (typeof sessionStorage === 'undefined') return;
  sessionStorage.setItem(DemoSessionKey, '1');
  sessionStorage.removeItem(LocalLoginSessionKey);
}

export function isDemoSession(): boolean {
  return typeof sessionStorage !== 'undefined' && sessionStorage.getItem(DemoSessionKey) === '1';
}

export function clearClientAuthMode(): void {
  if (typeof sessionStorage === 'undefined') return;
  sessionStorage.removeItem(LocalLoginSessionKey);
  sessionStorage.removeItem(DemoSessionKey);
}

@Injectable({ providedIn: 'root' })
export class DevelopmentIdentityStore {
  private readonly selectedKeyState = signal(this.readSelectedKey());

  readonly selectedKey = this.selectedKeyState.asReadonly();
  readonly selected = computed(
    () =>
      DEVELOPMENT_IDENTITIES.find((profile) => profile.key === this.selectedKeyState()) ??
      DEVELOPMENT_IDENTITIES[0]!,
  );

  select(key: string): boolean {
    if (!DEVELOPMENT_IDENTITIES.some((profile) => profile.key === key)) return false;
    this.selectedKeyState.set(key);
    this.storage()?.setItem(StorageKey, key);
    return true;
  }

  private readSelectedKey(): string {
    const key = this.storage()?.getItem(StorageKey);
    return DEVELOPMENT_IDENTITIES.some((profile) => profile.key === key)
      ? (key as string)
      : DEVELOPMENT_IDENTITIES[0]!.key;
  }

  private storage(): Storage | undefined {
    return typeof sessionStorage === 'undefined' ? undefined : sessionStorage;
  }
}

@Injectable({ providedIn: 'root' })
export class IdentityApi {
  private readonly http = inject(HttpClient);
  private readonly currentIdentity = this.http
    .get<CurrentIdentity>('/api/v1/me')
    .pipe(shareReplay({ bufferSize: 1, refCount: false }));

  me(): Observable<CurrentIdentity> {
    return this.currentIdentity;
  }

  login(request: { userName: string; password: string }): Observable<void> {
    return this.http.post<void>('/api/v1/auth/login', request);
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/v1/auth/logout', {});
  }
}

export const developmentIdentityInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) return next(request);
  if (typeof sessionStorage === 'undefined' || sessionStorage.getItem(DemoSessionKey) !== '1') {
    return next(request);
  }

  const identity = inject(DevelopmentIdentityStore).selected();
  return next(
    request.clone({
      setHeaders: {
        'X-Dev-User': identity.userId,
        'X-Dev-Name': identity.displayName,
        'X-Dev-Roles': identity.roles.join(','),
        'X-Dev-Locations': identity.locationScopes.join(','),
        'X-Dev-Competencies': identity.competencyCodes.join(','),
      },
    }),
  );
};
