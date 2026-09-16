import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PrintPackage, PrintPackageApi } from '../../core/print-package-api';

const STATUS_LABELS: Record<string, string> = {
  PENDING: 'Menunggu render',
  RETRYING: 'Mencoba ulang',
  READY: 'Siap dicetak',
  FAILED: 'Gagal dirender',
};

@Component({
  selector: 'app-permit-print-packages',
  imports: [DatePipe],
  templateUrl: './permit-print-packages.html',
  styleUrl: './permit-print-packages.scss',
})
export class PermitPrintPackages {
  readonly permitId = input.required<string>();
  readonly canRetry = input(false);
  readonly canPreview = input(false);

  /** Emits the ready packages so a signed field copy can be bound to one of them. */
  readonly readyPackages = output<{ id: string; permitVersion: number }[]>();

  protected readonly packages = signal<PrintPackage[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  private readonly api = inject(PrintPackageApi);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.load();
  }

  protected statusLabel(status: string): string {
    return STATUS_LABELS[status] ?? status;
  }

  protected download(item: PrintPackage): void {
    this.busy.set(true);
    this.error.set('');
    this.api
      .download(this.permitId(), item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.saveBlob(blob, `PTW-v${item.permitVersion}-${item.reference}.pdf`);
          this.busy.set(false);
        },
        error: (response) => {
          this.error.set(response?.error?.detail ?? 'Paket cetak gagal diunduh.');
          this.busy.set(false);
        },
      });
  }

  protected preview(): void {
    this.busy.set(true);
    this.error.set('');
    this.api
      .preview(this.permitId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.saveBlob(blob, 'PRATINJAU-DRAFT-TIDAK-BERLAKU.pdf');
          this.busy.set(false);
        },
        error: (response) => {
          this.error.set(response?.error?.detail ?? 'Pratinjau gagal dibuat.');
          this.busy.set(false);
        },
      });
  }

  protected retry(item: PrintPackage): void {
    this.busy.set(true);
    this.error.set('');
    this.api
      .retry(this.permitId(), item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.load();
        },
        error: (response) => {
          this.error.set(response?.error?.detail ?? 'Render ulang gagal dijadwalkan.');
          this.busy.set(false);
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.api
      .list(this.permitId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.packages.set(page.items);
          this.readyPackages.emit(
            page.items
              .filter((item) => item.renderStatus === 'READY')
              .map((item) => ({ id: item.id, permitVersion: item.permitVersion })),
          );
          this.loading.set(false);
        },
        error: () => {
          this.packages.set([]);
          this.loading.set(false);
        },
      });
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }
}
