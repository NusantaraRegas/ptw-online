import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { Observable } from 'rxjs';
import { authenticatedGuard } from './authentication.guard';
import { developmentIdentityInterceptor } from './development-identity';

describe('authenticatedGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([developmentIdentityInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    sessionStorage.clear();
  });

  it('redirects the initial protected route to the login screen', () => {
    const result = TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as never, { url: '/tasks' } as never),
    ) as UrlTree;

    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/login?returnUrl=%2Ftasks');
    TestBed.inject(HttpTestingController).expectNone('/api/v1/me');
  });

  it('verifies an explicitly selected demo session before opening the application', () => {
    sessionStorage.setItem('ptw.demo-session', '1');
    let allowed: boolean | UrlTree | undefined;
    const result = TestBed.runInInjectionContext(() =>
      authenticatedGuard({} as never, { url: '/' } as never),
    ) as Observable<boolean | UrlTree>;
    result.subscribe((value) => (allowed = value));

    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/me');
    expect(request.request.headers.get('X-Dev-User')).toBe('sponsor.demo');
    request.flush({ userId: 'sponsor.demo' });

    expect(allowed).toBe(true);
  });
});
