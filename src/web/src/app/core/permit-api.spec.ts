import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermitApi, PermitDraft } from './permit-api';

describe('PermitApi', () => {
  let api: PermitApi;
  let http: HttpTestingController;

  const draft: PermitDraft = {
    title: 'Perawatan pompa',
    description: 'Perawatan terencana',
    locationId: 'AREA-A',
    sponsorId: 'sponsor.demo',
    performingAuthority: 'Pelaksana Demo',
    company: 'PT Mitra',
    permitClass: 'ColdWork',
    riskLevel: 'Medium',
    validFrom: '2026-08-26T01:00:00.000Z',
    validUntil: '2026-08-26T09:00:00.000Z',
    hazards: ['Energi tersimpan'],
    controls: ['Isolasi energi'],
    requiredDocumentCodes: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PermitApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PermitApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads one permit by id', () => {
    api.get('permit-id').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id');
    expect(request.request.method).toBe('GET');
    request.flush({});
  });

  it('sends If-Match when updating a draft', () => {
    api.updateDraft('permit-id', draft, '"etag-value"').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/draft');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.body).toEqual(draft);
    request.flush({});
  });

  it('loads scoped activity with pagination', () => {
    api.listActivity('permit-id', 10, 5).subscribe();
    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/v1/permits/permit-id/activity' &&
        candidate.params.get('offset') === '10' &&
        candidate.params.get('limit') === '5',
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('loads immutable version history with pagination', () => {
    api.listVersions('permit-id', 0, 10).subscribe();
    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/v1/permits/permit-id/versions' &&
        candidate.params.get('offset') === '0' &&
        candidate.params.get('limit') === '10',
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('sends explicit validation command with concurrency and idempotency headers', () => {
    api.endorseHsse('permit-id', '"etag-value"', 'Persyaratan HSSE sesuai.').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/validations/hsse/endorse');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual({ statement: 'Persyaratan HSSE sesuai.' });
    request.flush({});
  });

  it('uses the issue command rather than exposing a generic status update', () => {
    const readiness = {
      eSimiEligible: true,
      locationVerified: true,
      toolboxTalkComplete: true,
      personnelAcknowledged: true,
      ppeAndControlsVerified: true,
      isolationVerified: true,
      simopsVerified: true,
      gasTestSatisfied: true,
      hasUnresolvedSuspension: false,
    };
    api.issue('permit-id', '"etag-value"', readiness).subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/issue');
    expect(request.request.body).toEqual(readiness);
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({});
  });
});
