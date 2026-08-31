import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  UserAuthorization,
  UserAuthorizationApi,
  UserAuthorizationDraft,
} from '../../core/user-authorization-api';

@Component({
  selector: 'app-admin-authorizations',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-authorizations.html',
  styleUrl: './admin-authorizations.scss',
})
export class AdminAuthorizations {
  private readonly api = inject(UserAuthorizationApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly assignments = signal<UserAuthorization[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly actingId = signal('');
  protected readonly error = signal('');
  protected readonly accessDenied = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly subjectCount = computed(
    () => new Set(this.assignments().map((item) => item.subjectId.toLowerCase())).size,
  );
  protected readonly effectiveCount = computed(
    () => this.assignments().filter((item) => item.isEffective).length,
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    subjectId: ['', [Validators.required, Validators.maxLength(200)]],
    roleCode: ['', [Validators.required, Validators.maxLength(100)]],
    actionCodes: ['', Validators.required],
    locationId: [''],
    includeDescendants: [false],
    requiredCompetencyCodes: [''],
    kind: ['DIRECT' as 'DIRECT' | 'DELEGATION', Validators.required],
    sourceAuthorizationId: [''],
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
      this.error.set('Lengkapi user ID, role, action, dan tanggal mulai efektif.');
      return;
    }

    const draft = this.toDraft();
    if (draft.kind === 'DELEGATION' && (!draft.sourceAuthorizationId || !draft.effectiveUntil)) {
      this.error.set('Delegasi wajib memiliki assignment sumber dan tanggal akhir efektif.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.api
      .create(draft)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entry) => {
          this.assignments.update((items) => [entry, ...items]);
          this.form.reset({ kind: 'DIRECT', includeDescendants: false });
          this.formOpen.set(false);
          this.saving.set(false);
        },
        error: (response) => this.handleError(response, 'Draft assignment gagal dibuat.'),
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
      PENDING_APPROVAL: 'Menunggu pemeriksa',
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

  private toDraft(): UserAuthorizationDraft {
    const value = this.form.getRawValue();
    return {
      subjectId: value.subjectId.trim(),
      roleCode: value.roleCode.trim(),
      actionCodes: this.splitCodes(value.actionCodes),
      locationId: value.locationId.trim() || null,
      includeDescendants: value.includeDescendants,
      requiredCompetencyCodes: this.splitCodes(value.requiredCompetencyCodes),
      kind: value.kind,
      sourceAuthorizationId: value.sourceAuthorizationId.trim() || null,
      effectiveFrom: new Date(value.effectiveFrom).toISOString(),
      effectiveUntil: value.effectiveUntil ? new Date(value.effectiveUntil).toISOString() : null,
    };
  }

  private splitCodes(value: string): string[] {
    return [
      ...new Set(
        value
          .split(',')
          .map((item) => item.trim())
          .filter(Boolean),
      ),
    ];
  }

  private handleError(response: any, fallback: string): void {
    this.saving.set(false);
    this.actingId.set('');
    this.error.set(response?.error?.detail ?? fallback);
  }
}
