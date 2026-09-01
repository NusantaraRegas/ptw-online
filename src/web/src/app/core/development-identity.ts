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
    key: 'hsse-validator',
    userId: 'hsse.validator.demo',
    displayName: 'Validator HSSE Demo',
    roles: ['HSSEValidator'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'gas-validator',
    userId: 'gas.validator.demo',
    displayName: 'Validator Distribusi Gas Demo',
    roles: ['GasDistributionValidator'],
    locationScopes: ['*'],
    competencyCodes: [],
  },
  {
    key: 'area-approver-ho',
    userId: 'area.approver.ho.demo',
    displayName: 'PIC Approver HO Demo',
    roles: ['AreaOwnerApprover'],
    locationScopes: ['HO'],
    competencyCodes: [],
  },
  {
    key: 'area-issuer-ho',
    userId: 'area.issuer.ho.demo',
    displayName: 'PIC Penerbit HO Demo',
    roles: ['IssuingAuthority'],
    locationScopes: ['HO'],
    competencyCodes: [],
  },
  {
    key: 'area-approver-orf',
    userId: 'area.approver.orf.demo',
    displayName: 'PIC Approver ORF & Site Office Demo',
    roles: ['AreaOwnerApprover'],
    locationScopes: ['ORF', 'SITE-OFFICE'],
    competencyCodes: [],
  },
  {
    key: 'area-issuer-orf',
    userId: 'area.issuer.orf.demo',
    displayName: 'PIC Penerbit ORF & Site Office Demo',
    roles: ['IssuingAuthority'],
    locationScopes: ['ORF', 'SITE-OFFICE'],
    competencyCodes: [],
  },
  {
    key: 'area-approver-fsru',
    userId: 'area.approver.fsru.demo',
    displayName: 'PIC Approver FSRU & Water-Based Demo',
    roles: ['AreaOwnerApprover'],
    locationScopes: ['FSRU', 'WATER-BASED-ACTIVITY'],
    competencyCodes: [],
  },
  {
    key: 'area-issuer-fsru',
    userId: 'area.issuer.fsru.demo',
    displayName: 'PIC Penerbit FSRU & Water-Based Demo',
    roles: ['IssuingAuthority'],
    locationScopes: ['FSRU', 'WATER-BASED-ACTIVITY'],
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
