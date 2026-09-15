import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PermitAttachmentApi } from '../../core/permit-attachment-api';
import { PermitAttachments } from './permit-attachments';

describe('PermitAttachments', () => {
  it('shows category and pending malware status for each file', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitAttachments],
      providers: [
        {
          provide: PermitAttachmentApi,
          useValue: {
            list: () =>
              of([
                {
                  id: 'attachment-id',
                  permitId: 'permit-id',
                  addedInVersion: 2,
                  removedInVersion: null,
                  fileName: 'risk-treatment.pdf',
                  sizeBytes: 2048,
                  mediaType: 'application/pdf',
                  sha256: 'a'.repeat(64),
                  scanStatus: 'PENDING',
                  scanEvidenceReference: null,
                  scannedAt: null,
                  category: 'SUPPORTING',
                  documentNumber: null,
                  documentRevision: null,
                  documentDate: null,
                  targetPermitVersion: 1,
                  printPackageId: null,
                  supersedesAttachmentId: null,
                  uploadedBy: 'sponsor.demo',
                  uploadedAt: '2026-09-04T12:00:00.000Z',
                },
              ]),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermitAttachments);
    fixture.componentRef.setInput('permitId', 'permit-id');
    fixture.componentRef.setInput('eTag', '"etag-value"');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('risk-treatment.pdf');
    expect(fixture.nativeElement.textContent).toContain('SUPPORTING · scan PENDING');
    expect((fixture.nativeElement.querySelector('.download') as HTMLButtonElement).disabled).toBe(
      true,
    );
  });
});
