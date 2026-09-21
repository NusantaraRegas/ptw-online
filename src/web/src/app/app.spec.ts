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
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/me').flush({
      userId: 'sponsor.demo',
      displayName: 'Sponsor Demo',
      roles: ['Sponsor', 'Administrator'],
      locationScopes: ['*'],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/tasks').flush({
      items: [
        {
          id: 'task-1',
          permitId: 'permit-1',
          permitVersion: 2,
          type: 'AREA_APPROVE_AND_ISSUE',
          label: 'Persetujuan PIC pemilik area',
          requiredRole: 'AreaOwnerApprover',
          status: 'PENDING',
          permitNumber: 'PTW-001',
          permitTitle: 'Perawatan pompa',
          locationId: 'FSRU',
          createdAt: '2026-09-04T12:00:00.000Z',
          completedAt: null,
        },
      ],
      count: 3,
    });
    fixture.detectChanges();

    expect(fixture.componentInstance).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Admin Maker Demo');
    expect(fixture.nativeElement.querySelector('.task-attention-badge')?.textContent).toContain(
      '3',
    );
    expect(
      fixture.nativeElement.querySelector('a[href="/tasks"]')?.getAttribute('aria-label'),
    ).toBe('Tugas Saya, 3 tugas perlu perhatian');
    expect(fixture.nativeElement.querySelector('.notification-button .unread-dot')).not.toBeNull();

    const notificationButton = fixture.nativeElement.querySelector(
      '.notification-button',
    ) as HTMLButtonElement;
    notificationButton.click();
    fixture.detectChanges();
    expect(notificationButton.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.notification-popover')?.textContent).toContain(
      'Persetujuan PIC pemilik area',
    );
    expect(
      fixture.nativeElement.querySelector('.notification-popover a[href="/permits/permit-1"]'),
    ).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.notification-all')?.getAttribute('href')).toBe(
      '/tasks',
    );
  });

  it('shows the persisted demo identity in the account selector', async () => {
    sessionStorage.setItem('ptw.development-identity', 'admin-checker');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/me').flush({
      userId: 'admin.checker.demo',
      displayName: 'Admin Checker Demo',
      roles: ['Administrator'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/tasks').flush({ items: [], count: 0 });
    fixture.detectChanges();

    const selector = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    expect(selector.value).toBe('admin-checker');
    expect(fixture.nativeElement.querySelector('.task-attention-badge')).toBeNull();
    expect(fixture.nativeElement.querySelector('.notification-button .unread-dot')).toBeNull();
    (fixture.nativeElement.querySelector('.notification-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.notification-popover')?.textContent).toContain(
      'Belum ada tugas aktif untuk akun ini.',
    );
    sessionStorage.clear();
  });

  it('shows a readable business label instead of an internal role code', async () => {
    sessionStorage.setItem('ptw.development-identity', 'area-owner-site-office');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
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
    http.expectOne('/api/v1/tasks').flush({ items: [], count: 0 });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.user small')?.textContent.trim()).toBe(
      'Manager Pemilik Wilayah',
    );
    expect(fixture.nativeElement.querySelector('.user')?.getAttribute('title')).toBe(
      'Manager Pemilik Wilayah Site-Office (General Affair) Demo',
    );
    expect(fixture.nativeElement.textContent).not.toContain('AreaOwnerManager');
    sessionStorage.clear();
  });
});
