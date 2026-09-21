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
});
