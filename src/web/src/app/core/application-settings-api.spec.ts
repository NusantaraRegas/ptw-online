import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApplicationSettingsApi, DemoModeSetting } from './application-settings-api';

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
});
