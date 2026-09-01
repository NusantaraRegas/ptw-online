import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  it('creates the PTW application shell', async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userId: 'sponsor.demo',
        displayName: 'Sponsor Demo',
        roles: ['Sponsor', 'Administrator'],
        locationScopes: ['*'],
        isDevelopmentIdentity: true,
      });
    fixture.detectChanges();

    expect(fixture.componentInstance).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Admin Maker Demo');
  });

  it('shows the persisted demo identity in the account selector', async () => {
    sessionStorage.setItem('ptw.development-identity', 'admin-checker');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/me')
      .flush({
        userId: 'admin.checker.demo',
        displayName: 'Admin Checker Demo',
        roles: ['Administrator'],
        locationScopes: ['*'],
        competencyCodes: [],
        isDevelopmentIdentity: true,
      });
    fixture.detectChanges();

    const selector = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    expect(selector.value).toBe('admin-checker');
    sessionStorage.clear();
  });
});
