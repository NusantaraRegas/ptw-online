import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermitPrintPackages } from './permit-print-packages';

function readyPackage(overrides: Record<string, unknown> = {}) {
  return {
    id: 'package-1',
    permitId: 'permit-1',
    permitVersion: 3,
    renderStatus: 'READY',
    attempts: 1,
    lastError: null,
    sizeBytes: 120_000,
    sha256: 'ABC',
    printTemplateVersion: 'FM-B-002-NR-B220/1',
    campaignAssetVersion: 'campaign-2026.09',
    reference: 'A1B2C3D4E5F6',
    createdAt: '2026-09-15T02:30:00Z',
    generatedAt: '2026-09-15T02:31:00Z',
    downloadable: true,
    ...overrides,
  };
}

describe('PermitPrintPackages', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows the ready package details without repeating the page-level safety notice', () => {
    const fixture = TestBed.createComponent(PermitPrintPackages);
    fixture.componentRef.setInput('permitId', 'permit-1');
    fixture.detectChanges();

    http.expectOne('/api/v1/permits/permit-1/print-packages').flush({
      items: [readyPackage()],
      count: 1,
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Diterbitkan belum otomatis mengizinkan pekerjaan dimulai');
    expect(text).toContain('Siap dicetak');
    expect(text).toContain('Ref. A1B2C3D4E5F6');
    expect(text).toContain('Unduh PDF');
  });

  it('emits only ready packages so field copies bind to a real printed sheet', () => {
    const fixture = TestBed.createComponent(PermitPrintPackages);
    fixture.componentRef.setInput('permitId', 'permit-1');
    let emitted: { id: string; permitVersion: number }[] = [];
    fixture.componentInstance.readyPackages.subscribe((value) => (emitted = value));
    fixture.detectChanges();

    http.expectOne('/api/v1/permits/permit-1/print-packages').flush({
      items: [
        readyPackage(),
        readyPackage({ id: 'package-2', renderStatus: 'FAILED', downloadable: false }),
      ],
      count: 2,
    });
    fixture.detectChanges();

    expect(emitted).toEqual([{ id: 'package-1', permitVersion: 3 }]);
  });

  it('disables download while a render is still pending', () => {
    const fixture = TestBed.createComponent(PermitPrintPackages);
    fixture.componentRef.setInput('permitId', 'permit-1');
    fixture.detectChanges();

    http.expectOne('/api/v1/permits/permit-1/print-packages').flush({
      items: [readyPackage({ renderStatus: 'PENDING', downloadable: false, generatedAt: null })],
      count: 1,
    });
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector(
      'button.download',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Menunggu render');
  });
});
