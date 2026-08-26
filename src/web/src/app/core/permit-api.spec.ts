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
});
