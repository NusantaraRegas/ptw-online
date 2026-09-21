import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';

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
  constructor(private readonly http: HttpClient) {}

  me(): Observable<CurrentIdentity> {
    return this.http.get<CurrentIdentity>('/api/v1/me');
  }
}

export const developmentIdentityInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) return next(request);

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
