import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, concatMap, defer, EMPTY, finalize, from, tap } from 'rxjs';
import {
  PermitAttachment,
  PermitAttachmentApi,
  PermitAttachmentCategory,
  PermitAttachmentMutation,
  PermitAttachmentUpload,
} from '../../core/permit-attachment-api';
import { PermitApi, PermitSupportingDocumentOption } from '../../core/permit-api';

interface PendingUpload {
  id: string;
  name: string;
  status: 'menunggu' | 'mengunggah' | 'selesai' | 'gagal';
  error?: string;
}

export interface PermitAttachmentPermitChange {
  eTag: string;
  version: number;
}

@Component({
  selector: 'app-permit-attachments',
  imports: [DatePipe],
  templateUrl: './permit-attachments.html',
  styleUrl: './permit-attachments.scss',
})
export class PermitAttachments {
  readonly permitId = input.required<string>();
  readonly eTag = input.required<string>();
  readonly canManage = input(false);
  readonly selectedDocumentCodes = input<string[]>([]);
  readonly jsaDocumentNumber = input('');
  readonly jsaRevision = input('');
  readonly jsaDate = input('');
  readonly permitChanged = output<PermitAttachmentPermitChange>();

  /** Ready print packages a signed field copy can be reconciled against. */
  readonly printPackages = input<{ id: string; permitVersion: number }[]>([]);

  protected readonly attachments = signal<PermitAttachment[]>([]);
  protected readonly pending = signal<PendingUpload[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly category = signal<PermitAttachmentCategory>('JSA');
  protected readonly supportingDocumentCode = signal('JSA');
  protected readonly supportingDocuments = signal<PermitSupportingDocumentOption[]>([]);
  protected readonly supportingDocumentError = signal('');
  protected readonly documentNumber = signal('');
  protected readonly documentRevision = signal('');
  protected readonly documentDate = signal('');
  protected readonly printPackageId = signal('');
  protected readonly hasUnavailableDownloads = computed(() =>
    this.attachments().some((attachment) => attachment.scanStatus !== 'CLEAN'),
  );
  protected readonly selectedSupportingDocuments = computed(() => {
    const selected = new Set(this.selectedDocumentCodes());
    return this.supportingDocuments().filter(
      (option) => option.required || selected.has(option.code),
    );
  });
  protected readonly selectedOptionalDocuments = computed(() =>
    this.selectedSupportingDocuments().filter((option) => !option.required),
  );

  private currentETag = '';

  constructor(
    private readonly api: PermitAttachmentApi,
    private readonly permitApi: PermitApi,
    private readonly destroyRef: DestroyRef,
  ) {}

  ngOnInit(): void {
    this.currentETag = this.eTag();
    this.prefillJsaMetadata();
    this.load();
    this.loadSupportingDocuments();
  }

  ngOnChanges(): void {
    this.currentETag = this.eTag();
  }

  protected selectFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (files.length === 0 || this.busy()) return;

    const invalid = files.filter((file) => !PermitAttachments.isAcceptedType(file));
    if (invalid.length > 0) {
      this.error.set(
        `Hanya PDF, JPEG, atau PNG yang dapat diunggah: ${invalid
          .map((file) => file.name)
          .join(', ')}`,
      );
      return;
    }

    const metadata = this.uploadMetadata();
    if (metadata === null) {
      this.error.set(
        'Kategori JSA dan Salinan Lapangan Bertanda Tangan memerlukan nomor, revisi, dan tanggal dokumen.',
      );
      return;
    }

    if (metadata.category === 'SIGNED_FIELD_COPY' && !metadata.printPackageId) {
      this.error.set('Salinan lapangan wajib dikaitkan dengan paket cetak resmi yang dicetak.');
      return;
    }

    this.error.set('');
    this.busy.set(true);
    const uploads = files.map((file) => ({ id: crypto.randomUUID(), file }));
    this.pending.set(uploads.map(({ id, file }) => ({ id, name: file.name, status: 'menunggu' })));
    from(uploads)
      .pipe(
        concatMap(({ id, file }) =>
          defer(() => {
            this.updatePending(id, 'mengunggah');
            return this.api.upload(this.permitId(), this.currentETag, file, metadata);
          }).pipe(
            tap((result) => {
              this.applyMutation(result);
              this.attachments.update((items) => [...items, result.attachment]);
              this.updatePending(id, 'selesai');
            }),
            catchError((response) => {
              const message = response?.error?.detail ?? 'File gagal diunggah.';
              this.updatePending(id, 'gagal', message);
              return EMPTY;
            }),
          ),
        ),
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  protected setCategory(value: string): void {
    const category = value as PermitAttachmentCategory;
    this.category.set(category);
    if (category === 'JSA') {
      this.supportingDocumentCode.set('JSA');
      this.prefillJsaMetadata();
    } else if (category === 'SIGNED_FIELD_COPY') {
      this.supportingDocumentCode.set('');
    } else if (
      !this.selectedOptionalDocuments().some(
        (option) => option.code === this.supportingDocumentCode(),
      )
    ) {
      this.supportingDocumentCode.set('');
    }
    this.error.set('');
  }

  protected attachmentsFor(documentCode: string): PermitAttachment[] {
    return this.attachments().filter(
      (attachment) =>
        attachment.supportingDocumentCode === documentCode ||
        (documentCode === 'JSA' && attachment.category === 'JSA'),
    );
  }

  protected documentStatus(documentCode: string): string {
    const evidence = this.attachmentsFor(documentCode);
    if (evidence.some((attachment) => attachment.scanStatus === 'CLEAN')) return 'Aman';
    if (evidence.length > 0) return 'Sedang diperiksa';
    return 'Belum diunggah';
  }

  protected documentComplete(documentCode: string): boolean {
    return this.attachmentsFor(documentCode).length > 0;
  }

  protected documentLabel(attachment: PermitAttachment): string {
    const code = attachment.supportingDocumentCode ?? (attachment.category === 'JSA' ? 'JSA' : '');
    return this.supportingDocuments().find((option) => option.code === code)?.label ?? '';
  }

  /** The server accepts PDF, JPEG and PNG; the picker must not be stricter than the contract. */
  private static isAcceptedType(file: File): boolean {
    const accepted = ['application/pdf', 'image/jpeg', 'image/png'];
    const extensions = ['.pdf', '.jpg', '.jpeg', '.png'];
    const name = file.name.toLocaleLowerCase();
    return (
      (file.type === '' || accepted.includes(file.type)) &&
      extensions.some((extension) => name.endsWith(extension))
    );
  }

  private prefillJsaMetadata(): void {
    if (!this.documentNumber()) this.documentNumber.set(this.jsaDocumentNumber());
    if (!this.documentRevision()) this.documentRevision.set(this.jsaRevision());
    if (!this.documentDate()) this.documentDate.set(this.jsaDate().slice(0, 10));
  }

  /** Returns null when the chosen category is missing its mandatory document metadata. */
  private uploadMetadata(): PermitAttachmentUpload | null {
    const category = this.category();
    if (category === 'SUPPORTING') {
      return {
        category,
        supportingDocumentCode: this.supportingDocumentCode() || null,
      };
    }

    const documentNumber = this.documentNumber().trim();
    const documentRevision = this.documentRevision().trim();
    const documentDate = this.documentDate();
    if (!documentNumber || !documentRevision || !documentDate) {
      return null;
    }

    return {
      category,
      supportingDocumentCode: category === 'JSA' ? 'JSA' : null,
      documentNumber,
      documentRevision,
      documentDate,
      printPackageId: category === 'SIGNED_FIELD_COPY' ? this.printPackageId() || null : null,
    };
  }

  protected remove(attachment: PermitAttachment): void {
    if (this.busy() || !globalThis.confirm(`Hapus ${attachment.fileName} dari draft PTW?`)) return;
    this.busy.set(true);
    this.error.set('');
    this.api
      .remove(this.permitId(), attachment.id, this.currentETag)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.applyMutation(result);
          this.attachments.update((items) => items.filter((item) => item.id !== attachment.id));
        },
        error: (response) =>
          this.error.set(response?.error?.detail ?? 'Lampiran gagal dihapus dari draft.'),
      });
  }

  protected download(attachment: PermitAttachment): void {
    this.api
      .download(this.permitId(), attachment.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = attachment.fileName;
          anchor.click();
          URL.revokeObjectURL(url);
        },
        error: (response) => this.error.set(response?.error?.detail ?? 'Lampiran gagal diunduh.'),
      });
  }

  protected formatSize(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.ceil(bytes / 1024)} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected categoryLabel(category: PermitAttachmentCategory): string {
    switch (category) {
      case 'JSA':
        return 'JSA';
      case 'SIGNED_FIELD_COPY':
        return 'Salinan lapangan';
      default:
        return 'Dokumen pendukung';
    }
  }

  protected scanLabel(status: string): string {
    switch (status) {
      case 'CLEAN':
        return 'Aman';
      case 'INFECTED':
        return 'Diblokir';
      case 'FAILED':
        return 'Pemeriksaan gagal';
      default:
        return 'Sedang diperiksa';
    }
  }

  private load(): void {
    this.loading.set(true);
    this.api
      .list(this.permitId())
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (items) => this.attachments.set(items),
        error: (response) =>
          this.error.set(response?.error?.detail ?? 'Daftar lampiran gagal dimuat.'),
      });
  }

  private loadSupportingDocuments(): void {
    this.permitApi
      .listSupportingDocuments()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => this.supportingDocuments.set(options),
        error: (response) =>
          this.supportingDocumentError.set(
            response?.error?.detail ?? 'Status checklist dokumen pendukung gagal dimuat.',
          ),
      });
  }

  private applyMutation(result: PermitAttachmentMutation): void {
    this.currentETag = result.eTag;
    this.permitChanged.emit({ eTag: result.eTag, version: result.permitVersion });
  }

  private updatePending(id: string, status: PendingUpload['status'], error?: string): void {
    this.pending.update((items) =>
      items.map((item) => (item.id === id ? { ...item, status, error } : item)),
    );
  }
}
