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

    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/tasks').flush({
      items: [
        {
          id: '10000000-0000-0000-0000-000000000001',
          permitId: '20000000-0000-0000-0000-000000000002',
          permitVersion: 3,
          type: 'HSSE_VALIDATION',
          label: 'Validasi HSSE',
          requiredRole: 'HSSEValidator',
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
    expect(task.textContent).toContain('Validasi HSSE');
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

    http.expectOne('/api/v1/permits').flush({ items: [], count: 0 });
    http.expectOne('/api/v1/tasks').flush({ items: [], count: 0 });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Tidak ada tugas aktif');
    expect(fixture.nativeElement.querySelector('.task-row')).toBeNull();
  });
});
