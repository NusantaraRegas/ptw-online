import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LocationDraft, LocationMasterApi } from './location-master-api';

describe('LocationMasterApi', () => {
  let api: LocationMasterApi;
  let http: HttpTestingController;

  const draft: LocationDraft = {
    code: 'AREA-01',
    name: 'Area Satu',
    parentId: null,
    effectiveFrom: '2026-08-31T00:00:00.000Z',
    effectiveUntil: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LocationMasterApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(LocationMasterApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('creates an effective-dated location draft', () => {
    api.create(draft).subscribe();
    const request = http.expectOne('/api/v1/admin/locations');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(draft);
    request.flush({});
  });

  it('sends concurrency and idempotency headers for submit', () => {
    api.submit('location-id', '"location-v1"').subscribe();
    const request = http.expectOne('/api/v1/admin/locations/location-id/submit');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"location-v1"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({});
  });
});
