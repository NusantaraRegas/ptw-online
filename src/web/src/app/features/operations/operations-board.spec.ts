import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OperationsBoard } from './operations-board';

describe('OperationsBoard', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders ORF area-owner global monitoring with business labels and a safety warning', async () => {
    await TestBed.configureTestingModule({
      imports: [OperationsBoard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(OperationsBoard);
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
    http.expectOne('/api/v1/locations').flush({
      items: [{ id: 'location-orf', code: 'ORF', name: 'Onshore Receiving Facility' }],
      count: 1,
    });
    http
      .expectOne(
        (request) =>
          request.url === '/api/v1/operations' &&
          request.params.get('offset') === '0' &&
          request.params.get('limit') === '25',
      )
      .flush({
        metrics: { issued: 3, suspended: 1, expiringSoon: 1, closureRequested: 2 },
        items: [
          {
            id: 'permit-suspended',
            permitNumber: 'PTW-20260925-001',
            title: 'Penggantian valve inlet',
            company: 'PT Mitra Aman',
            locationId: 'ORF',
            permitClass: 'HotWork',
            status: 'SUSPENDED',
            validFrom: '2026-09-24T00:00:00Z',
            validUntil: '2026-09-26T00:00:00Z',
            updatedAt: '2026-09-25T00:00:00Z',
            suspensionReason: 'Gas detector memberi alarm.',
          },
        ],
        count: 1,
        generatedAt: '2026-09-25T00:00:00Z',
      });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(text).toContain('Diterbitkan belum otomatis mengizinkan pekerjaan dimulai');
    expect(text).toContain('Seluruh PTW non-draft');
    expect(text).toContain('seluruh wilayah');
    expect(text).toContain('Onshore Receiving Facility');
    expect(text).toContain('Pekerjaan Panas');
    expect(text).toContain('Pekerjaan harus dihentikan');
    expect(text).toContain('Gas detector memberi alarm.');
    expect(fixture.nativeElement.querySelector('.permit-row')?.getAttribute('href')).toBe(
      '/permits/permit-suspended',
    );
    expect(fixture.nativeElement.querySelector('.class-mark')?.dataset['permitClass']).toBe(
      'HotWork',
    );
    expect(text).not.toContain('AreaOwnerManager');
    expect(
      Array.from<HTMLOptionElement>(
        fixture.nativeElement.querySelectorAll('select[formControlName="status"] option'),
      ).map((option) => option.value),
    ).toContain('CLOSED');
  });

  it('sends status filters to the scoped operations endpoint', async () => {
    await TestBed.configureTestingModule({
      imports: [OperationsBoard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(OperationsBoard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'hse.demo',
      displayName: 'PIC HSE',
      roles: ['HSEValidator'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: true,
    });
    http.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    http
      .expectOne((request) => request.url === '/api/v1/operations')
      .flush({
        metrics: { issued: 0, suspended: 0, expiringSoon: 0, closureRequested: 0 },
        items: [],
        count: 0,
        generatedAt: '2026-09-25T00:00:00Z',
      });

    const status = fixture.nativeElement.querySelector(
      'select[formControlName="status"]',
    ) as HTMLSelectElement;
    status.value = 'SUSPENDED';
    status.dispatchEvent(new Event('change'));
    await new Promise((resolve) => setTimeout(resolve, 260));

    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/v1/operations' && candidate.params.get('status') === 'SUSPENDED',
    );
    request.flush({
      metrics: { issued: 0, suspended: 0, expiringSoon: 0, closureRequested: 0 },
      items: [],
      count: 0,
      generatedAt: '2026-09-25T00:00:00Z',
    });
  });

  it('changes the number of items per page and resets pagination', async () => {
    await TestBed.configureTestingModule({
      imports: [OperationsBoard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(OperationsBoard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const response = {
      metrics: { issued: 0, suspended: 0, expiringSoon: 0, closureRequested: 0 },
      items: [],
      count: 60,
      generatedAt: '2026-09-25T00:00:00Z',
    };

    http.expectOne('/api/v1/me').flush({
      userId: 'superadmin.local',
      displayName: 'Super Administrator',
      roles: ['Administrator'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: false,
    });
    http.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    http
      .expectOne(
        (request) =>
          request.url === '/api/v1/operations' &&
          request.params.get('offset') === '0' &&
          request.params.get('limit') === '25',
      )
      .flush(response);
    fixture.detectChanges();

    const next = fixture.nativeElement.querySelectorAll(
      '.pagination button',
    )[1] as HTMLButtonElement;
    next.click();
    http
      .expectOne(
        (request) =>
          request.url === '/api/v1/operations' &&
          request.params.get('offset') === '25' &&
          request.params.get('limit') === '25',
      )
      .flush(response);
    fixture.detectChanges();

    const pageSize = fixture.nativeElement.querySelector(
      'select[formControlName="pageSize"]',
    ) as HTMLSelectElement;
    expect(Array.from(pageSize.options).map((option) => option.textContent?.trim())).toEqual([
      '10 item',
      '25 item',
      '50 item',
      '100 item',
    ]);
    pageSize.selectedIndex = 0;
    pageSize.dispatchEvent(new Event('change'));
    await new Promise((resolve) => setTimeout(resolve, 260));

    http
      .expectOne(
        (request) =>
          request.url === '/api/v1/operations' &&
          request.params.get('offset') === '0' &&
          request.params.get('limit') === '10',
      )
      .flush(response);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pagination')?.textContent).toContain(
      'Menampilkan 1–10 dari 60 PTW',
    );
  });

  it('shows every non-draft status to administrators, including closed permits', async () => {
    await TestBed.configureTestingModule({
      imports: [OperationsBoard],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(OperationsBoard);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/v1/me').flush({
      userId: 'superadmin.local',
      displayName: 'Super Administrator',
      roles: ['Administrator'],
      locationScopes: ['*'],
      competencyCodes: [],
      isDevelopmentIdentity: false,
    });
    http.expectOne('/api/v1/locations').flush({ items: [], count: 0 });
    http
      .expectOne((request) => request.url === '/api/v1/operations')
      .flush({
        metrics: { issued: 0, suspended: 0, expiringSoon: 0, closureRequested: 0 },
        items: [
          {
            id: 'permit-closed',
            permitNumber: 'PTW-20260925-099',
            title: 'Pekerjaan selesai',
            company: 'PT Mitra Aman',
            locationId: 'ORF',
            permitClass: 'ColdWork',
            status: 'CLOSED',
            validFrom: '2026-09-20T00:00:00Z',
            validUntil: '2026-09-24T00:00:00Z',
            updatedAt: '2026-09-25T00:00:00Z',
            suspensionReason: null,
          },
        ],
        count: 1,
        generatedAt: '2026-09-25T00:00:00Z',
      });
    fixture.detectChanges();

    const statusOptions = Array.from(
      fixture.nativeElement.querySelectorAll('select[formControlName="status"] option'),
    ).map((option) => (option as HTMLOptionElement).value);
    const text = fixture.nativeElement.textContent.replace(/\s+/g, ' ').trim();
    expect(statusOptions).toContain('CLOSED');
    expect(statusOptions).not.toContain('DRAFT');
    expect(text).toContain('Seluruh PTW non-draft');
    expect(text).toContain('Ditutup');
    expect(text).toContain('Proses PTW telah ditutup');
  });
});
