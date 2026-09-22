import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ApplicationSettingsApi, DemoModeSetting } from '../../core/application-settings-api';
import { clearClientAuthMode, isDemoSession } from '../../core/development-identity';

@Component({
  selector: 'app-admin-settings',
  imports: [DatePipe, RouterLink],
  templateUrl: './admin-settings.html',
  styleUrl: './admin-settings.scss',
})
export class AdminSettings {
  private readonly api = inject(ApplicationSettingsApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly setting = signal<DemoModeSetting | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly accessDenied = signal(false);

  constructor() {
    this.load();
  }

  protected toggle(event: Event): void {
    const enabled = (event.target as HTMLInputElement).checked;
    const current = this.setting();
    if (!current) return;

    this.saving.set(true);
    this.error.set('');
    this.success.set('');
    this.api
      .setDemoMode(current, enabled)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.setting.set(updated);
          this.saving.set(false);
          this.success.set(
            updated.enabled ? 'Mode demo telah diaktifkan.' : 'Mode demo telah dinonaktifkan.',
          );
          if (!updated.enabled && isDemoSession()) {
            clearClientAuthMode();
            globalThis.location.assign('/login');
          }
        },
        error: (response) => {
          this.saving.set(false);
          this.error.set(response?.error?.detail ?? 'Pengaturan gagal diperbarui.');
          this.load();
        },
      });
  }

  private load(): void {
    this.api
      .demoMode()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (setting) => {
          this.setting.set(setting);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.accessDenied.set(response?.status === 403);
          this.error.set(response?.error?.detail ?? 'Pengaturan aplikasi gagal dimuat.');
        },
      });
  }
}
