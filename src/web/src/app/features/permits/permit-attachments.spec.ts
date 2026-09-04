import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PermitAttachmentApi } from '../../core/permit-attachment-api';
import { PermitAttachments } from './permit-attachments';

describe('PermitAttachments', () => {
  it('does not show a per-file malware warning while scanning is out of scope', async () => {
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
                  scanStatus: 'NOT_SCANNED',
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
    expect(fixture.nativeElement.textContent).not.toContain('Belum dipindai malware');
  });
});
