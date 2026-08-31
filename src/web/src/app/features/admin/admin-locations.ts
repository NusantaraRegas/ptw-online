import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LocationDraft, LocationMaster, LocationMasterApi } from '../../core/location-master-api';

@Component({
  selector: 'app-admin-locations',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-locations.html',
  styleUrl: './admin-locations.scss',
})
export class AdminLocations {
  private readonly api = inject(LocationMasterApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly locations = signal<LocationMaster[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly actingId = signal('');
  protected readonly error = signal('');
  protected readonly accessDenied = signal(false);
  protected readonly formOpen = signal(false);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(100)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    parentId: [''],
    effectiveFrom: ['', Validators.required],
    effectiveUntil: [''],
  });

  constructor() {
    this.load();
  }

  protected toggleForm(): void {
    this.formOpen.update((value) => !value);
    this.error.set('');
  }

  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Lengkapi kode, nama, dan tanggal mulai efektif.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.api
      .create(this.toDraft())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entry) => {
          this.locations.update((items) => [entry, ...items]);
          this.form.reset();
          this.formOpen.set(false);
          this.saving.set(false);
        },
        error: (response) => this.handleError(response, 'Draft lokasi gagal dibuat.'),
      });
  }

  protected submit(entry: LocationMaster): void {
    this.runCommand(entry, 'submit');
  }

  protected approve(entry: LocationMaster): void {
    this.runCommand(entry, 'approve');
  }

  protected statusLabel(status: LocationMaster['status']): string {
    return {
      DRAFT: 'Draft',
      PENDING_APPROVAL: 'Menunggu pemeriksa',
      APPROVED: 'Disetujui',
    }[status];
  }

  protected parentName(parentId: string | null): string {
    if (!parentId) return 'Lokasi induk';
    return this.locations().find((item) => item.id === parentId)?.name ?? 'Induk tidak tersedia';
  }

  private load(): void {
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.locations.set(page.items);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.accessDenied.set(response?.status === 403);
          this.error.set(response?.error?.detail ?? 'Master lokasi gagal dimuat.');
        },
      });
  }

  private runCommand(entry: LocationMaster, command: 'submit' | 'approve'): void {
    this.actingId.set(entry.id);
    this.error.set('');
    const request =
      command === 'submit'
        ? this.api.submit(entry.id, entry.eTag)
        : this.api.approve(entry.id, entry.eTag);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.locations.update((items) =>
          items.map((item) => (item.id === updated.id ? updated : item)),
        );
        this.actingId.set('');
      },
      error: (response) => this.handleError(response, 'Status master lokasi gagal diperbarui.'),
    });
  }

  private toDraft(): LocationDraft {
    const value = this.form.getRawValue();
    return {
      code: value.code,
      name: value.name,
      parentId: value.parentId || null,
      effectiveFrom: new Date(value.effectiveFrom).toISOString(),
      effectiveUntil: value.effectiveUntil ? new Date(value.effectiveUntil).toISOString() : null,
    };
  }

  private handleError(response: any, fallback: string): void {
    this.saving.set(false);
    this.actingId.set('');
    this.error.set(response?.error?.detail ?? fallback);
  }
}
