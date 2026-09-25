import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PermitList } from './permit-list';

describe('PermitList', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('hides draft creation for an area owner role', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitList);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'area.owner.orf.demo',
      displayName: 'Manager Pemilik Wilayah ORF Demo',
      roles: ['AreaOwnerManager'],
      locationScopes: ['ORF'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href="/permits/new"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain(
      'Belum ada PTW yang tersedia untuk akun dan cakupan lokasi Anda.',
    );
  });

  it('keeps draft creation available for a Sponsor', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitList);
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
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href="/permits/new"]')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Belum ada PTW. Buat draft pertama Anda.');
  });

  it('uses distinct semantic colors for different PTW statuses', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitList);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'area.owner.orf.demo',
      displayName: 'Manager Pemilik Wilayah ORF Demo',
      roles: ['AreaOwnerManager'],
      locationScopes: ['ORF'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    const statuses = ['ISSUED', 'CLOSED', 'REJECTED', 'SUSPENDED'];
    const permitClasses = ['HotWork', 'ColdWork', 'ConfinedSpaceEntry', 'HotWork'];
    http.expectOne('/api/v1/permits').flush({
      items: statuses.map((status, index) => ({
        id: `permit-${index}`,
        permitNumber: `PTW-${index}`,
        status,
        updatedAt: '2026-09-23T08:00:00Z',
        draft: {
          permitClass: permitClasses[index],
          title: `Permit ${status}`,
          locationId: 'ORF',
          company: 'PT Kontraktor',
        },
      })),
      count: statuses.length,
    });
    fixture.detectChanges();

    const badges = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('.permit-status'),
    );
    expect(badges.map((badge) => badge.dataset['status'])).toEqual(statuses);
    expect(badges.map((badge) => badge.textContent?.trim())).toEqual([
      'Diterbitkan',
      'Ditutup',
      'Ditolak',
      'Ditangguhkan',
    ]);
    const classBadges = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('.permit-class'),
    );
    expect(classBadges.map((badge) => badge.dataset['permitClass'])).toEqual(permitClasses);
    expect(classBadges.map((badge) => badge.textContent?.trim())).toEqual([
      'HW',
      'CW',
      'CSE',
      'HW',
    ]);
  });

  it('searches PTW through the API after the user stops typing', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(PermitList);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'sponsor.only.demo',
      displayName: 'Sponsor Only Demo',
      roles: ['Sponsor'],
      locationScopes: ['ORF'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });

    const search = fixture.nativeElement.querySelector('#permit-search') as HTMLInputElement;
    search.value = 'pengelasan pipa';
    search.dispatchEvent(new Event('input'));
    http.expectNone((request) => request.params.has('search'));
    await new Promise((resolve) => setTimeout(resolve, 310));

    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/v1/permits' && candidate.params.get('search') === 'pengelasan pipa',
    );
    request.flush({
      items: [
        {
          id: 'matching-permit',
          permitNumber: 'PTW-2026-001',
          status: 'DRAFT',
          updatedAt: '2026-09-23T08:00:00Z',
          draft: {
            permitClass: 'HotWork',
            title: 'Pengelasan Pipa',
            locationId: 'ORF',
            company: 'PT Kontraktor',
          },
        },
      ],
      count: 1,
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.permit-item')).toHaveLength(1);
    expect(fixture.nativeElement.textContent).toContain('1 PTW');
    expect(fixture.nativeElement.textContent).toContain('hasil pencarian');
  });
});
