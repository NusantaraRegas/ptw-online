import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { Observable } from 'rxjs';
import { operationsBoardGuard } from './operations-board.guard';

describe('operationsBoardGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('allows a scoped area owner', () => {
    let result: boolean | UrlTree | undefined;
    const guard = TestBed.runInInjectionContext(() =>
      operationsBoardGuard({} as never, {} as never),
    ) as Observable<boolean | UrlTree>;
    guard.subscribe((value) => (result = value));

    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userId: 'area.owner.orf',
        roles: ['AreaOwnerManager'],
        locationScopes: ['ORF'],
      });

    expect(result).toBe(true);
  });

  it('redirects a Sponsor-only identity to the dashboard', () => {
    let result: boolean | UrlTree | undefined;
    const guard = TestBed.runInInjectionContext(() =>
      operationsBoardGuard({} as never, {} as never),
    ) as Observable<boolean | UrlTree>;
    guard.subscribe((value) => (result = value));

    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userId: 'sponsor.only',
        roles: ['Sponsor'],
        locationScopes: ['ORF'],
      });

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });
});
