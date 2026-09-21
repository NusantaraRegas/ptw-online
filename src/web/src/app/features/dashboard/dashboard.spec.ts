import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Dashboard } from './dashboard';

describe('Dashboard', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders real workflow tasks as links to their permits', async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Dashboard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'hse.validator.demo',
      displayName: 'PIC HSE Demo',
      roles: ['HSEValidator'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/tasks').flush({
      items: [
        {
          id: '10000000-0000-0000-0000-000000000001',
          permitId: '20000000-0000-0000-0000-000000000002',
          permitVersion: 3,
          type: 'HSE_VALIDATION',
          label: 'Validasi PIC HSE',
          requiredRole: 'HSEValidator',
          status: 'PENDING',
          permitNumber: 'PTW-20260904-0001',
          permitTitle: 'Perawatan compressor',
          locationId: 'ORF',
          createdAt: '2026-09-04T01:30:00Z',
          completedAt: null,
        },
      ],
      count: 1,
    });
    fixture.detectChanges();

    const task = fixture.nativeElement.querySelector('.task-row') as HTMLAnchorElement;
    expect(task.textContent).toContain('Validasi PIC HSE');
    expect(task.textContent).toContain('PTW-20260904-0001');
    expect(task.getAttribute('href')).toBe('/permits/20000000-0000-0000-0000-000000000002');
    expect(fixture.nativeElement.textContent).not.toContain('Review Hot Work');
    expect(fixture.nativeElement.textContent).not.toContain('Perbaiki dokumen JSA');
  });

  it('shows an explicit empty state when the actor has no pending tasks', async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Dashboard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'area.owner.site-office.demo',
      displayName: 'Manager Pemilik Wilayah Site-Office (General Affair) Demo',
      roles: ['AreaOwnerManager'],
      locationScopes: ['SITE_OFFICE'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/tasks').flush({ items: [], count: 0 });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Tidak ada tugas aktif');
    expect(fixture.nativeElement.querySelector('.task-row')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/permits/new"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('h1')?.textContent.trim()).toBe('Selamat datang');
    expect(fixture.nativeElement.querySelector('.account-context')?.textContent).toContain(
      'Manager Pemilik Wilayah Site-Office',
    );
    expect(fixture.nativeElement.textContent).toContain(
      'Belum ada PTW yang tersedia untuk akun dan cakupan lokasi Anda.',
    );
  });

  it('shows the create action only for Sponsor or Administrator identities', async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(Dashboard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/tasks').flush({ items: [], count: 0 });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href="/permits/new"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain(
      'Buat PTW pertama untuk memulai alur perencanaan.',
    );
  });
});
