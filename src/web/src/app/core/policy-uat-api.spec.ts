import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PolicyUatApi, PolicyUatSuiteDraft } from './policy-uat-api';

describe('PolicyUatApi', () => {
  let api: PolicyUatApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PolicyUatApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PolicyUatApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads immutable UAT suites', () => {
    api.listSuites().subscribe();
    const request = http.expectOne('/api/v1/admin/policy-uat-suites');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('creates and runs suites with idempotency keys', () => {
    const draft: PolicyUatSuiteDraft = {
      suiteKey: 'OPN-002-BASELINE',
      name: 'Baseline authorization',
      policyVersion: 'draft-v1',
      scenarios: [],
    };

    api.createSuite(draft).subscribe();
    const create = http.expectOne('/api/v1/admin/policy-uat-suites');
    expect(create.request.method).toBe('POST');
    expect(create.request.headers.has('Idempotency-Key')).toBe(true);
    create.flush({});

    api.runSuite('suite-1').subscribe();
    const run = http.expectOne('/api/v1/admin/policy-uat-suites/suite-1/runs');
    expect(run.request.method).toBe('POST');
    expect(run.request.headers.has('Idempotency-Key')).toBe(true);
    run.flush({});
  });
});
