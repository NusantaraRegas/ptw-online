import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  DEVELOPMENT_IDENTITIES,
  DevelopmentIdentityStore,
  developmentIdentityInterceptor,
} from './development-identity';

describe('developmentIdentityInterceptor', () => {
  let httpClient: HttpClient;
  let httpTesting: HttpTestingController;
  let identityStore: DevelopmentIdentityStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([developmentIdentityInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpClient = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    identityStore = TestBed.inject(DevelopmentIdentityStore);
  });

  afterEach(() => {
    httpTesting.verify();
    sessionStorage.clear();
  });

  it('sends the selected maker identity on API requests', () => {
    expect(identityStore.select('admin-maker')).toBe(true);

    httpClient.get('/api/v1/me').subscribe();
    const request = httpTesting.expectOne('/api/v1/me');

    expect(request.request.headers.get('X-Dev-User')).toBe('admin.maker.demo');
    expect(request.request.headers.get('X-Dev-Name')).toBe('Admin Maker Demo');
    expect(request.request.headers.get('X-Dev-Roles')).toBe('Administrator');
    expect(request.request.headers.get('X-Dev-Locations')).toBe('*');
    expect(request.request.headers.get('X-Dev-Competencies')).toBe('');
    request.flush({});
  });

  it('rejects unknown identity keys', () => {
    expect(identityStore.select('unknown')).toBe(false);
    expect(identityStore.selectedKey()).toBe('sponsor-admin');
  });

  it('uses one area owner actor for approval and issuance', () => {
    const areaOwner = DEVELOPMENT_IDENTITIES.find((item) => item.key === 'area-owner-orf');

    expect(areaOwner?.roles).toEqual(['AreaOwnerApprover', 'IssuingAuthority']);
    expect(areaOwner?.locationScopes).toEqual(['ORF', 'SITE-OFFICE']);
  });
});
