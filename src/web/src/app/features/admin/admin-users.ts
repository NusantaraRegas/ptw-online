import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  CreateUser,
  UpdateUser,
  UserAccount,
  UserDirectoryApi,
} from '../../core/user-directory-api';

@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-users.html',
  styleUrl: './admin-users.scss',
})
export class AdminUsers {
  private readonly api = inject(UserDirectoryApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly users = signal<UserAccount[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly actingId = signal('');
  protected readonly error = signal('');
  protected readonly accessDenied = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingUser = signal<UserAccount | null>(null);
  protected readonly form = this.formBuilder.nonNullable.group({
    subjectId: ['', [Validators.required, Validators.maxLength(200)]],
    userName: ['', [Validators.required, Validators.maxLength(100)]],
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    position: ['', Validators.maxLength(200)],
    department: ['', Validators.maxLength(200)],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
  });

  constructor() {
    this.load();
  }

  protected toggleForm(): void {
    if (this.formOpen()) {
      this.closeForm();
      return;
    }
    this.editingUser.set(null);
    this.form.reset();
    this.form.controls.password.setValidators([
      Validators.required,
      Validators.minLength(12),
      Validators.maxLength(128),
    ]);
    this.form.controls.password.updateValueAndValidity();
    this.formOpen.set(true);
    this.error.set('');
  }

  protected edit(user: UserAccount): void {
    this.editingUser.set(user);
    this.form.reset({
      subjectId: user.subjectId,
      userName: user.userName,
      displayName: user.displayName,
      position: user.position ?? '',
      department: user.department ?? '',
      password: '',
    });
    this.form.controls.password.clearValidators();
    this.form.controls.password.updateValueAndValidity();
    this.formOpen.set(true);
    this.error.set('');
  }

  protected closeForm(): void {
    this.formOpen.set(false);
    this.editingUser.set(null);
    this.form.reset();
    this.error.set('');
  }

  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Lengkapi identitas dan password pengguna.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    const editing = this.editingUser();
    const request = editing
      ? this.api.update(editing, this.toUpdateRequest())
      : this.api.create(this.toRequest());
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (user) => {
        if (editing) {
          this.replace(user);
        } else {
          this.users.update((items) => [user, ...items]);
        }
        this.closeForm();
        this.saving.set(false);
      },
      error: (response) =>
        this.handleError(
          response,
          editing ? 'Profil pengguna gagal diperbarui.' : 'Pengguna gagal dibuat.',
        ),
    });
  }

  protected setActive(user: UserAccount): void {
    this.actingId.set(user.subjectId);
    this.error.set('');
    this.api
      .setActive(user, !user.isActive)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => this.replace(updated),
        error: (response) => this.handleError(response, 'Status pengguna gagal diperbarui.'),
      });
  }

  protected selectSignature(user: UserAccount, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.actingId.set(user.subjectId);
    this.error.set('');
    this.api
      .uploadSignature(user, file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          input.value = '';
          this.replace(updated);
        },
        error: (response) => {
          input.value = '';
          this.handleError(response, 'Tanda tangan gagal diunggah.');
        },
      });
  }

  protected signatureUrl(user: UserAccount): string {
    return this.api.signatureUrl(user);
  }

  private load(): void {
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.users.set(page.items);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.accessDenied.set(response?.status === 403);
          this.error.set(response?.error?.detail ?? 'Daftar pengguna gagal dimuat.');
        },
      });
  }

  private toRequest(): CreateUser {
    const value = this.form.getRawValue();
    return {
      subjectId: value.subjectId.trim(),
      userName: value.userName.trim(),
      displayName: value.displayName.trim(),
      position: value.position.trim() || null,
      department: value.department.trim() || null,
      password: value.password,
    };
  }

  private toUpdateRequest(): UpdateUser {
    const value = this.form.getRawValue();
    return {
      displayName: value.displayName.trim(),
      position: value.position.trim() || null,
      department: value.department.trim() || null,
    };
  }

  private replace(updated: UserAccount): void {
    this.users.update((items) =>
      items.map((item) => (item.subjectId === updated.subjectId ? updated : item)),
    );
    this.actingId.set('');
  }

  private handleError(response: any, fallback: string): void {
    this.saving.set(false);
    this.actingId.set('');
    this.error.set(response?.error?.detail ?? fallback);
  }
}
