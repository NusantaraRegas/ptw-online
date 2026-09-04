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

  it('loads workflow tasks scoped by the server', () => {
    api.listTasks().subscribe();
    const request = http.expectOne('/api/v1/tasks');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('sends If-Match when updating a draft', () => {
    api.updateDraft('permit-id', draft, '"etag-value"').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/draft');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.body).toEqual(draft);
    request.flush({});
  });

  it('requests renewal as a new permit with concurrency and idempotency headers', () => {
    const renewal = {
      validFrom: '2026-08-26T09:00:00.000Z',
      validUntil: '2026-08-26T17:00:00.000Z',
    };
    api.requestRenewal('permit-id', '"etag-value"', renewal).subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/renewals');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual(renewal);
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

  it('sends an explicit revision command with a mandatory reason', () => {
    api.requestRevision('permit-id', '"etag-value"', 'Kontrol perlu diperbaiki.').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/request-revision');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual({ reason: 'Kontrol perlu diperbaiki.' });
    request.flush({});
  });

  it('sends an explicit reject command instead of a generic status update', () => {
    api.reject('permit-id', '"etag-value"', 'Risiko residual tidak diterima.').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/reject');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual({ reason: 'Risiko residual tidak diterima.' });
    request.flush({});
  });

  it('sends the fail-safe suspension request and area-owner approval commands', () => {
    api.requestSuspension('permit-id', '"etag-value"', 'Kondisi lapangan berubah.').subscribe();
    const suspensionRequest = http.expectOne('/api/v1/permits/permit-id/suspensions/request');
    expect(suspensionRequest.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(suspensionRequest.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(suspensionRequest.request.body).toEqual({ reason: 'Kondisi lapangan berubah.' });
    suspensionRequest.flush({});

    api.approveSuspension('permit-id', '"etag-next"', 'Penangguhan disetujui.').subscribe();
    const suspensionApproval = http.expectOne('/api/v1/permits/permit-id/suspensions/approve');
    expect(suspensionApproval.request.headers.get('If-Match')).toBe('"etag-next"');
    expect(suspensionApproval.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(suspensionApproval.request.body).toEqual({ statement: 'Penangguhan disetujui.' });
    suspensionApproval.flush({});
  });

  it('uses explicit three-party completion and close commands', () => {
    api.declareCompletion('permit-id', '"etag-1"', 'Pekerjaan selesai.').subscribe();
    const declaration = http.expectOne('/api/v1/permits/permit-id/completion/declare');
    expect(declaration.request.body).toEqual({ statement: 'Pekerjaan selesai.' });
    expect(declaration.request.headers.get('Idempotency-Key')).toBeTruthy();
    declaration.flush({});

    api.confirmHsseCompletion('permit-id', '"etag-2"', 'Kondisi akhir aman.').subscribe();
    const hsse = http.expectOne('/api/v1/permits/permit-id/completion/confirm/hsse');
    expect(hsse.request.body).toEqual({ statement: 'Kondisi akhir aman.' });
    expect(hsse.request.headers.get('If-Match')).toBe('"etag-2"');
    hsse.flush({});

    api.confirmAreaOwnerCompletion('permit-id', '"etag-3"', 'Area diterima kembali.').subscribe();
    const areaOwner = http.expectOne('/api/v1/permits/permit-id/completion/confirm/area-owner');
    expect(areaOwner.request.body).toEqual({ statement: 'Area diterima kembali.' });
    expect(areaOwner.request.headers.get('If-Match')).toBe('"etag-3"');
    areaOwner.flush({});

    api.close('permit-id', '"etag-4"', 'PTW ditutup.').subscribe();
    const close = http.expectOne('/api/v1/permits/permit-id/close');
    expect(close.request.body).toEqual({ statement: 'PTW ditutup.' });
    expect(close.request.headers.get('If-Match')).toBe('"etag-4"');
    close.flush({});
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
