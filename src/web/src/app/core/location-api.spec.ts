import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LocationApi } from './location-api';

describe('LocationApi', () => {
  let api: LocationApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LocationApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(LocationApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads selectable locations from the scoped lookup endpoint', () => {
    api.list().subscribe();
    const request = http.expectOne('/api/v1/locations');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });
});
