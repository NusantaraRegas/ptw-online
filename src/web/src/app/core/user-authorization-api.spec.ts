import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UserAuthorizationApi, UserAuthorizationDraft } from './user-authorization-api';

describe('UserAuthorizationApi', () => {
  let api: UserAuthorizationApi;
  let http: HttpTestingController;

  const draft: UserAuthorizationDraft = {
    subjectId: 'operator.satu',
    roleCode: 'PTW_ISSUER',
    actionCodes: ['permit.issue'],
    locationId: null,
    includeDescendants: false,
    requiredCompetencyCodes: [],
    kind: 'DIRECT',
    sourceAuthorizationId: null,
    effectiveFrom: '2026-08-31T00:00:00.000Z',
    effectiveUntil: '2026-09-30T00:00:00.000Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [UserAuthorizationApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(UserAuthorizationApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('creates a multi-role assignment draft', () => {
    api.create(draft).subscribe();
    const request = http.expectOne('/api/v1/admin/authorizations');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(draft);
    request.flush({});
  });

  it('creates a controlled direct assignment without client-supplied action codes', () => {
    const direct = {
      subjectId: 'area.manager',
      roleCode: 'AreaOwnerManager',
      locationId: 'location-id',
      effectiveFrom: '2026-09-21T00:00:00.000Z',
      effectiveUntil: null,
    };

    api.createDirect(direct).subscribe();
    const request = http.expectOne('/api/v1/admin/authorizations/direct');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(direct);
    expect(request.request.body.actionCodes).toBeUndefined();
    request.flush({});
  });

  it('loads controlled role options from the server', () => {
    api.listDirectRoleOptions().subscribe();
    const request = http.expectOne('/api/v1/admin/authorizations/direct-role-options');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('sends concurrency and idempotency headers for approval', () => {
    api.approve('assignment-id', '"authorization-v2"').subscribe();
    const request = http.expectOne('/api/v1/admin/authorizations/assignment-id/approve');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"authorization-v2"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({});
  });
});
