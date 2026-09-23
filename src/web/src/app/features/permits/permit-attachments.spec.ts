import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PermitAttachmentApi } from '../../core/permit-attachment-api';
import { PermitApi } from '../../core/permit-api';
import { PermitAttachments } from './permit-attachments';

const supportingDocuments = [
  {
    code: 'JSA',
    label: 'Job Safety Analisis (JSA)',
    templateColumn: 0,
    templateIndex: 0,
    required: true,
    requiresMetadata: true,
  },
  {
    code: 'WORK_PROCEDURE',
    label: 'Prosedur Pekerjaan',
    templateColumn: 0,
    templateIndex: 3,
    required: false,
    requiresMetadata: false,
  },
];

const mandatoryDocuments = [
  {
    code: 'JSA',
    label: 'Job Safety Analisis (JSA)',
    uploadCategory: 'JSA',
    requiresMetadata: true,
  },
  {
    code: 'WORK_PROCEDURE',
    label: 'Prosedur Pekerjaan',
    uploadCategory: 'SUPPORTING',
    requiresMetadata: false,
  },
  { code: 'ID', label: 'ID', uploadCategory: 'SUPPORTING', requiresMetadata: false },
  {
    code: 'BPJS_TK',
    label: 'BPJS TK',
    uploadCategory: 'SUPPORTING',
    requiresMetadata: false,
  },
  { code: 'FTW', label: 'FTW', uploadCategory: 'SUPPORTING', requiresMetadata: false },
  {
    code: 'ESIMI',
    label: 'E-SIMI',
    uploadCategory: 'SUPPORTING',
    requiresMetadata: false,
  },
];

describe('PermitAttachments', () => {
  it('shows a readable category without exposing the internal scan status', async () => {
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
                  uploadedByName: 'Siti Sponsor',
                  uploadedAt: '2026-09-04T12:00:00.000Z',
                },
              ]),
          },
        },
        {
          provide: PermitApi,
          useValue: {
            listSupportingDocuments: () => of(supportingDocuments),
            listMandatoryDocuments: () => of(mandatoryDocuments),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermitAttachments);
    fixture.componentRef.setInput('permitId', 'permit-id');
    fixture.componentRef.setInput('eTag', '"etag-value"');
    fixture.componentRef.setInput('selectedDocumentCodes', ['JSA', 'WORK_PROCEDURE']);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('risk-treatment.pdf');
    expect(fixture.nativeElement.textContent).toContain('JSA terkontrol');
    expect(fixture.nativeElement.textContent).toContain('Dokumen wajib pengajuan');
    expect(fixture.nativeElement.textContent).toContain('Prosedur Pekerjaan');
    expect(fixture.nativeElement.textContent).not.toContain('Dokumen tambahan Bagian 4');
    expect(fixture.nativeElement.textContent).toContain('BPJS TK');
    expect(fixture.nativeElement.textContent).toContain('Dokumen pendukung');
    expect(fixture.nativeElement.textContent).toContain('diunggah oleh Siti Sponsor');
    expect(fixture.nativeElement.textContent).not.toContain('sponsor.demo');
    expect(fixture.nativeElement.textContent).not.toContain('Sedang diperiksa');
    expect(fixture.nativeElement.textContent).toContain('belum tersedia untuk diunduh');
    expect((fixture.nativeElement.querySelector('.download') as HTMLButtonElement).disabled).toBe(
      true,
    );
  });

  it('shows a helpful empty state and upload action to users who can manage attachments', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitAttachments],
      providers: [
        {
          provide: PermitAttachmentApi,
          useValue: { list: () => of([]) },
        },
        {
          provide: PermitApi,
          useValue: {
            listSupportingDocuments: () => of(supportingDocuments),
            listMandatoryDocuments: () => of(mandatoryDocuments),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermitAttachments);
    fixture.componentRef.setInput('permitId', 'permit-id');
    fixture.componentRef.setInput('eTag', '"etag-value"');
    fixture.componentRef.setInput('canManage', true);
    fixture.componentRef.setInput('jsaDocumentNumber', 'JSA-001');
    fixture.componentRef.setInput('jsaRevision', '2');
    fixture.componentRef.setInput('jsaDate', '2026-09-16T00:00:00.000Z');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.empty-state')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Belum ada lampiran');
    expect(fixture.nativeElement.textContent).toContain('Tambah lampiran');
    expect((fixture.nativeElement.querySelector('select') as HTMLSelectElement).value).toBe('JSA');
    expect(fixture.nativeElement.textContent).not.toContain('Salinan lapangan bertanda tangan');
    const metadataValues = Array.from<HTMLInputElement>(
      fixture.nativeElement.querySelectorAll('.upload-metadata input'),
    ).map((input) => input.value);
    expect(metadataValues).toEqual(['JSA-001', '2', '2026-09-16']);
  });

  it('only offers signed field copy upload in the post-issuance field-copy mode', async () => {
    await TestBed.configureTestingModule({
      imports: [PermitAttachments],
      providers: [
        {
          provide: PermitAttachmentApi,
          useValue: { list: () => of([]) },
        },
        {
          provide: PermitApi,
          useValue: {
            listSupportingDocuments: () => of(supportingDocuments),
            listMandatoryDocuments: () => of(mandatoryDocuments),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermitAttachments);
    fixture.componentRef.setInput('permitId', 'permit-id');
    fixture.componentRef.setInput('eTag', '"etag-value"');
    fixture.componentRef.setInput('canManage', true);
    fixture.componentRef.setInput('fieldCopyOnly', true);
    fixture.componentRef.setInput('printPackages', [{ id: 'print-package-id', permitVersion: 7 }]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Dokumen lapangan');
    expect(fixture.nativeElement.textContent).toContain('Unggah salinan bertanda tangan');
    expect(fixture.nativeElement.textContent).not.toContain('Kategori lampiran');
    expect(fixture.nativeElement.textContent).not.toContain('Dokumen wajib pengajuan');
    expect((fixture.nativeElement.querySelector('select') as HTMLSelectElement).value).toBe(
      'print-package-id',
    );
  });

  it('makes the signed copy selected for closure the primary PTW download', async () => {
    const attachments = [
      {
        id: 'supporting-id',
        permitId: 'permit-id',
        addedInVersion: 1,
        removedInVersion: null,
        fileName: 'jsa.pdf',
        sizeBytes: 2048,
        mediaType: 'application/pdf',
        sha256: 'a'.repeat(64),
        scanStatus: 'CLEAN',
        scanEvidenceReference: 'scan-1',
        scannedAt: '2026-09-04T12:00:00.000Z',
        category: 'JSA' as const,
        supportingDocumentCode: 'JSA',
        documentNumber: 'JSA-001',
        documentRevision: '1',
        documentDate: '2026-09-04T00:00:00.000Z',
        targetPermitVersion: 1,
        printPackageId: null,
        supersedesAttachmentId: null,
        uploadedBy: 'sponsor.demo',
        uploadedByName: 'Sponsor Demo',
        uploadedAt: '2026-09-04T12:00:00.000Z',
      },
      {
        id: 'signed-copy-id',
        permitId: 'permit-id',
        addedInVersion: 1,
        removedInVersion: null,
        fileName: 'ptw-signed.pdf',
        sizeBytes: 4096,
        mediaType: 'application/pdf',
        sha256: 'b'.repeat(64),
        scanStatus: 'CLEAN',
        scanEvidenceReference: 'scan-2',
        scannedAt: '2026-09-04T13:00:00.000Z',
        category: 'SIGNED_FIELD_COPY' as const,
        supportingDocumentCode: null,
        documentNumber: 'PTW-001',
        documentRevision: '1',
        documentDate: '2026-09-04T00:00:00.000Z',
        targetPermitVersion: 1,
        printPackageId: 'print-package-id',
        supersedesAttachmentId: null,
        uploadedBy: 'sponsor.demo',
        uploadedByName: 'Sponsor Demo',
        uploadedAt: '2026-09-04T13:00:00.000Z',
      },
    ];
    await TestBed.configureTestingModule({
      imports: [PermitAttachments],
      providers: [
        { provide: PermitAttachmentApi, useValue: { list: () => of(attachments) } },
        {
          provide: PermitApi,
          useValue: {
            listSupportingDocuments: () => of(supportingDocuments),
            listMandatoryDocuments: () => of(mandatoryDocuments),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermitAttachments);
    fixture.componentRef.setInput('permitId', 'permit-id');
    fixture.componentRef.setInput('eTag', '"etag-value"');
    fixture.componentRef.setInput('fieldCopyOnly', true);
    fixture.componentRef.setInput('primaryAttachmentIds', ['signed-copy-id']);
    fixture.detectChanges();

    const primary = fixture.nativeElement.querySelector(
      '.attachment-list li.primary-document',
    ) as HTMLElement | null;
    expect(fixture.nativeElement.textContent).toContain('Dokumen utama PTW');
    expect(primary?.textContent).toContain('ptw-signed.pdf');
    const primaryDownload = primary?.querySelector('.primary-download') as HTMLButtonElement | null;
    expect(primaryDownload?.textContent).toContain('Unduh untuk review');
    expect(getComputedStyle(primaryDownload!).color).toBe('rgb(255, 255, 255)');
    expect(getComputedStyle(primaryDownload!).backgroundColor).toBe('rgb(21, 112, 71)');
    expect(fixture.nativeElement.querySelector('.attachment-list li')).toBe(primary);
  });
});
