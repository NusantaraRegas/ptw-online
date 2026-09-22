import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Login } from './login';

describe('Login', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  async function render(enabled: boolean) {
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/auth/options')
      .flush({ demoModeEnabled: enabled });
    fixture.detectChanges();
    return fixture;
  }

  it('shows demo entry when the server enables demo mode', async () => {
    const fixture = await render(true);
    expect(fixture.nativeElement.querySelector('.demo-button')?.textContent).toContain(
      'Gunakan mode demo',
    );
  });

  it('hides demo entry when the server disables demo mode', async () => {
    const fixture = await render(false);
    expect(fixture.nativeElement.querySelector('.demo-button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.login-divider')).toBeNull();
  });
});
