import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, switchMap } from 'rxjs';
import {
  CreateUser,
  UpdateUser,
  UserAccount,
  UserDirectoryApi,
} from '../../core/user-directory-api';

/** Mirrors the server policy so the form can explain requirements; the server remains the authority. */
export interface PasswordRequirement {
  key: 'length' | 'upper' | 'lower' | 'digit' | 'username';
  label: string;
  met: boolean;
}

const PASSWORD_MIN_LENGTH = 12;
const PASSWORD_MAX_LENGTH = 128;

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
    password: [
      '',
      [Validators.required, (control: AbstractControl) => this.passwordPolicy(control)],
    ],
  });

  private readonly passwordValue = toSignal(this.form.controls.password.valueChanges, {
    initialValue: '',
  });
  private readonly userNameValue = toSignal(this.form.controls.userName.valueChanges, {
    initialValue: '',
  });
  protected readonly passwordRequirements = computed<PasswordRequirement[]>(() =>
    evaluatePassword(this.passwordValue() ?? '', this.userNameValue() ?? ''),
  );

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
    // A new account always needs an initial local password.
    this.form.controls.password.setValidators([
      Validators.required,
      (control) => this.passwordPolicy(control),
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
    // Editing keeps the current password unless a new one is typed; a typed value must still
    // satisfy the same policy the server enforces.
    this.form.controls.password.setValidators([(control) => this.passwordPolicy(control)]);
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
      this.error.set(
        this.form.controls.password.invalid && this.editingUser()
          ? 'Password lokal baru belum memenuhi syarat.'
          : 'Lengkapi identitas dan password pengguna.',
      );
      return;
    }

    this.saving.set(true);
    this.error.set('');
    const editing = this.editingUser();
    const newPassword = this.form.controls.password.value;
    let request: Observable<UserAccount>;
    if (editing) {
      request = this.api.update(editing, this.toUpdateRequest());
      if (newPassword) {
        // The profile update returns a fresh ETag, which the password change must carry.
        request = request.pipe(
          switchMap((updated) => this.api.resetPassword(updated, newPassword)),
        );
      }
    } else {
      request = this.api.create(this.toRequest());
    }
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
          editing
            ? newPassword
              ? 'Profil atau password pengguna gagal diperbarui.'
              : 'Profil pengguna gagal diperbarui.'
            : 'Pengguna gagal dibuat.',
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

  protected passwordInvalid(): boolean {
    const control = this.form.controls.password;
    return control.invalid && (control.touched || control.dirty);
  }

  private passwordPolicy(control: AbstractControl): ValidationErrors | null {
    const value = String(control.value ?? '');
    if (!value) return null;
    const userName = String(this.form?.controls.userName.value ?? '');
    return evaluatePassword(value, userName).every((item) => item.met)
      ? null
      : { passwordPolicy: true };
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

export function evaluatePassword(password: string, userName: string): PasswordRequirement[] {
  const normalizedUserName = userName.trim();
  const containsUserName =
    normalizedUserName.length >= 4 &&
    password.toLowerCase().includes(normalizedUserName.toLowerCase());
  return [
    {
      key: 'length',
      label: `${PASSWORD_MIN_LENGTH}-${PASSWORD_MAX_LENGTH} karakter`,
      met: password.length >= PASSWORD_MIN_LENGTH && password.length <= PASSWORD_MAX_LENGTH,
    },
    // Unicode classes match the server's char.IsUpper/IsLower/IsDigit checks.
    { key: 'upper', label: 'Memuat huruf besar', met: /\p{Lu}/u.test(password) },
    { key: 'lower', label: 'Memuat huruf kecil', met: /\p{Ll}/u.test(password) },
    { key: 'digit', label: 'Memuat angka', met: /\p{Nd}/u.test(password) },
    {
      key: 'username',
      label: 'Tidak memuat username',
      met: password.length > 0 && !containsUserName,
    },
  ];
}
