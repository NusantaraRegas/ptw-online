import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OperationalPolicyApi } from './operational-policy-api';

describe('OperationalPolicyApi', () => {
  let api: OperationalPolicyApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [OperationalPolicyApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(OperationalPolicyApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the fail-closed policy preflight', () => {
    api.readiness().subscribe();
    const request = http.expectOne('/api/v1/admin/policy-readiness');
    expect(request.request.method).toBe('GET');
    request.flush({});
  });

  it('posts a non-mutating policy simulation scenario', () => {
    const scenario = {
      subjectId: 'operator.satu',
      actionCode: 'permit.submit',
      locationCode: 'AREA-01',
      competencyCodes: ['HSE_INDUCTION'],
      evaluatedAt: null,
    };

    api.simulate(scenario).subscribe();
    const request = http.expectOne('/api/v1/admin/policy-simulations');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(scenario);
    request.flush({});
  });
});
