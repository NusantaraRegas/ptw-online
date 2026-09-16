import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PrintPackageApi } from './print-package-api';

describe('PrintPackageApi', () => {
  let api: PrintPackageApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PrintPackageApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists print packages with their render status', () => {
    api.list('permit-1').subscribe();

    const request = http.expectOne('/api/v1/permits/permit-1/print-packages');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('downloads the official document as a blob', () => {
    api.download('permit-1', 'package-1').subscribe();

    const request = http.expectOne('/api/v1/permits/permit-1/print-packages/package-1/content');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob());
  });

  it('requests the watermarked draft preview from a separate endpoint', () => {
    api.preview('permit-1').subscribe();

    const request = http.expectOne('/api/v1/permits/permit-1/print-packages/preview');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob());
  });

  it('sends an idempotency key when requeueing a render', () => {
    api.retry('permit-1', 'package-1').subscribe();

    const request = http.expectOne('/api/v1/permits/permit-1/print-packages/package-1/retry');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.has('Idempotency-Key')).toBe(true);
    request.flush({});
  });
});
