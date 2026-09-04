import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermitAttachmentApi } from './permit-attachment-api';

describe('PermitAttachmentApi', () => {
  let api: PermitAttachmentApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PermitAttachmentApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uploads one PDF with concurrency and idempotency headers', () => {
    const file = new File(['%PDF-1.7'], 'jsa.pdf', { type: 'application/pdf' });
    api.upload('permit-1', '"etag-1"', file).subscribe();

    const request = http.expectOne('/api/v1/permits/permit-1/attachments');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-1"');
    expect(request.request.headers.has('Idempotency-Key')).toBe(true);
    const uploaded = request.request.body.get('file') as File;
    expect(uploaded.name).toBe('jsa.pdf');
    expect(uploaded.type).toBe('application/pdf');
    request.flush({
      attachment: {},
      permitVersion: 2,
      eTag: '"etag-2"',
    });
  });

  it('removes an attachment through an explicit command', () => {
    api.remove('permit-1', 'attachment-1', '"etag-2"').subscribe();

    const request = http.expectOne('/api/v1/permits/permit-1/attachments/attachment-1/remove');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-2"');
    expect(request.request.headers.has('Idempotency-Key')).toBe(true);
    request.flush({ attachment: {}, permitVersion: 3, eTag: '"etag-3"' });
  });
});
