import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  DirectUserAuthorizationDraft,
  UserAuthorization,
  UserAuthorizationApi,
  UserAuthorizationRoleOption,
} from '../../core/user-authorization-api';
import { UserAccount, UserDirectoryApi } from '../../core/user-directory-api';
import { LocationApi, LocationOption } from '../../core/location-api';

@Component({
  selector: 'app-admin-authorizations',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-authorizations.html',
  styleUrl: './admin-authorizations.scss',
})
export class AdminAuthorizations {
  private readonly api = inject(UserAuthorizationApi);
  private readonly userApi = inject(UserDirectoryApi);
  private readonly locationApi = inject(LocationApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly assignments = signal<UserAuthorization[]>([]);
  protected readonly users = signal<UserAccount[]>([]);
  protected readonly locations = signal<LocationOption[]>([]);
  protected readonly roleOptions = signal<UserAuthorizationRoleOption[]>([]);
  protected readonly selectedRoleCode = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly actingId = signal('');
  protected readonly error = signal('');
  protected readonly accessDenied = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingAssignment = signal<UserAuthorization | null>(null);
  protected readonly subjectCount = computed(
    () => new Set(this.assignments().map((item) => item.subjectId.toLowerCase())).size,
  );
  protected readonly effectiveCount = computed(
    () => this.assignments().filter((item) => item.isEffective).length,
  );
  protected readonly selectedRole = computed(() =>
    this.roleOptions().find((item) => item.code === this.selectedRoleCode()),
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    subjectId: ['', [Validators.required, Validators.maxLength(200)]],
    roleCode: ['', [Validators.required, Validators.maxLength(100)]],
    locationId: [''],
    effectiveFrom: [this.localDateTimeValue(), Validators.required],
    effectiveUntil: [''],
    neverExpires: [true],
  });

  constructor() {
    this.load();
    this.userApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => this.users.set(page.items.filter((user) => user.isActive)) });
    this.locationApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => this.locations.set(page.items) });
    this.api
      .listDirectRoleOptions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => this.roleOptions.set(page.items) });
  }

  protected toggleForm(): void {
    if (this.formOpen()) {
      this.closeForm();
      return;
    }
    this.editingAssignment.set(null);
    this.form.reset({ effectiveFrom: this.localDateTimeValue(), neverExpires: true });
    this.selectedRoleCode.set('');
    this.formOpen.set(true);
    this.error.set('');
  }

  protected edit(entry: UserAuthorization): void {
    if (entry.status === 'PENDING_APPROVAL' || entry.kind !== 'DIRECT') return;
    this.editingAssignment.set(entry);
    this.selectedRoleCode.set(entry.roleCode);
    this.form.reset({
      subjectId: entry.subjectId,
      roleCode: entry.roleCode,
      locationId: entry.locationId ?? '',
      effectiveFrom: this.localDateTimeValue(new Date(entry.effectiveFrom)),
      effectiveUntil: entry.effectiveUntil
        ? this.localDateTimeValue(new Date(entry.effectiveUntil))
        : '',
      neverExpires: entry.effectiveUntil === null,
    });
    this.formOpen.set(true);
    this.error.set('');
  }

  protected closeForm(): void {
    this.formOpen.set(false);
    this.editingAssignment.set(null);
    this.selectedRoleCode.set('');
    this.error.set('');
  }

  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Lengkapi pengguna, role, dan tanggal mulai efektif.');
      return;
    }

    const draft = this.toDraft();
    if (this.selectedRole()?.locationRequired && !draft.locationId) {
      this.error.set('Pilih area kewenangan untuk role Pemilik Wilayah.');
      return;
    }
    if (!this.form.controls.neverExpires.value && !draft.effectiveUntil) {
      this.error.set('Isi akhir efektif atau pilih Tanpa tanggal berakhir.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    const editing = this.editingAssignment();
    const request = !editing
      ? this.api.createDirect(draft)
      : editing.status === 'APPROVED'
        ? this.api.reviseDirect(editing, draft)
        : this.api.updateDirectDraft(editing, draft);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (entry) => {
        this.assignments.update((items) =>
          editing ? items.map((item) => (item.id === entry.id ? entry : item)) : [entry, ...items],
        );
        this.closeForm();
        this.saving.set(false);
      },
      error: (response) =>
        this.handleError(
          response,
          editing ? 'Assignment gagal diperbarui.' : 'Draft assignment gagal dibuat.',
        ),
    });
  }

  protected submit(entry: UserAuthorization): void {
    this.runCommand(entry, 'submit');
  }

  protected approve(entry: UserAuthorization): void {
    this.runCommand(entry, 'approve');
  }

  protected statusLabel(status: UserAuthorization['status']): string {
    return {
      DRAFT: 'Draft',
      PENDING_APPROVAL: 'Menunggu persetujuan',
      APPROVED: 'Disetujui',
    }[status];
  }

  protected initials(roleCode: string): string {
    return roleCode
      .split(/[_\s-]+/)
      .slice(0, 2)
      .map((part) => part.charAt(0))
      .join('')
      .toUpperCase();
  }

  protected selectRole(roleCode: string): void {
    this.selectedRoleCode.set(roleCode);
    this.form.controls.locationId.setValue('');
  }

  protected roleLabel(roleCode: string): string {
    return this.roleOptions().find((item) => item.code === roleCode)?.label ?? roleCode;
  }

  protected locationLabel(locationId: string | null): string {
    if (!locationId) return 'Seluruh lokasi';
    const location = this.locations().find((item) => item.id === locationId);
    return location?.name ?? 'Lokasi tidak tersedia';
  }

  protected selectedUserLabel(): string {
    const subjectId = this.form.controls.subjectId.value;
    const user = this.users().find((item) => item.subjectId === subjectId);
    return user?.displayName ?? subjectId;
  }

  private load(): void {
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.assignments.set(page.items);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.accessDenied.set(response?.status === 403);
          this.error.set(response?.error?.detail ?? 'Assignment otorisasi gagal dimuat.');
        },
      });
  }

  private runCommand(entry: UserAuthorization, command: 'submit' | 'approve'): void {
    this.actingId.set(entry.id);
    this.error.set('');
    const request =
      command === 'submit'
        ? this.api.submit(entry.id, entry.eTag)
        : this.api.approve(entry.id, entry.eTag);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.assignments.update((items) =>
          items.map((item) => (item.id === updated.id ? updated : item)),
        );
        this.actingId.set('');
      },
      error: (response) =>
        this.handleError(response, 'Status assignment otorisasi gagal diperbarui.'),
    });
  }

  private toDraft(): DirectUserAuthorizationDraft {
    const value = this.form.getRawValue();
    return {
      subjectId: value.subjectId.trim(),
      roleCode: value.roleCode.trim(),
      locationId: value.locationId.trim() || null,
      effectiveFrom: new Date(value.effectiveFrom).toISOString(),
      effectiveUntil:
        value.neverExpires || !value.effectiveUntil
          ? null
          : new Date(value.effectiveUntil).toISOString(),
    };
  }

  private localDateTimeValue(date = new Date()): string {
    return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  }

  private handleError(response: any, fallback: string): void {
    this.saving.set(false);
    this.actingId.set('');
    this.error.set(response?.error?.detail ?? fallback);
  }
}
