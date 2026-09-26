import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ApplicationSettingsApi,
  DemoModeSetting,
  UserGuideSetting,
} from './application-settings-api';

describe('ApplicationSettingsApi', () => {
  let api: ApplicationSettingsApi;
  let http: HttpTestingController;

  const setting: DemoModeSetting = {
    enabled: true,
    updatedAt: null,
    updatedBy: null,
    eTag: '"0"',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ApplicationSettingsApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApplicationSettingsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads public login options', () => {
    api.publicOptions().subscribe();
    const request = http.expectOne('/api/v1/auth/options');
    expect(request.request.method).toBe('GET');
    request.flush({ demoModeEnabled: true });
  });

  it('sends concurrency and idempotency headers when disabling demo mode', () => {
    api.setDemoMode(setting, false).subscribe();
    const request = http.expectOne('/api/v1/admin/settings/demo-mode/disable');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"0"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({ ...setting, enabled: false, eTag: '"1"' });
  });

  it('downloads the user guide as a blob', () => {
    api.downloadUserGuide().subscribe();
    const request = http.expectOne('/api/v1/user-guide/content');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob());
  });

  it('uploads a replacement user guide with concurrency and idempotency headers', () => {
    const guide: UserGuideSetting = {
      available: true,
      fileName: 'panduan.pdf',
      sizeBytes: 20,
      sha256: 'A'.repeat(64),
      version: 1,
      updatedAt: null,
      updatedBy: null,
      eTag: '"1"',
    };
    const file = new File(['%PDF-1.7'], 'panduan-baru.pdf', { type: 'application/pdf' });
    api.replaceUserGuide(guide, file).subscribe();

    const request = http.expectOne('/api/v1/admin/settings/user-guide');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"1"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect((request.request.body as FormData).get('file')).toBe(file);
    request.flush({ ...guide, fileName: file.name, version: 2, eTag: '"2"' });
  });
});
