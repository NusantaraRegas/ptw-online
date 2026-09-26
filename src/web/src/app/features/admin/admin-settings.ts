import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  ApplicationSettingsApi,
  DemoModeSetting,
  UserGuideSetting,
} from '../../core/application-settings-api';
import { clearClientAuthMode, isDemoSession } from '../../core/development-identity';

@Component({
  selector: 'app-admin-settings',
  imports: [DatePipe, DecimalPipe, RouterLink],
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
  protected readonly userGuide = signal<UserGuideSetting | null>(null);
  protected readonly guideLoading = signal(true);
  protected readonly guideSaving = signal(false);
  protected readonly guideError = signal('');
  protected readonly guideSuccess = signal('');
  protected readonly selectedGuide = signal<File | null>(null);

  constructor() {
    this.load();
    this.loadUserGuide();
  }

  protected selectGuide(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.item(0) ?? null;
    this.selectedGuide.set(file);
    this.guideError.set('');
    this.guideSuccess.set('');
  }

  protected uploadGuide(fileInput: HTMLInputElement): void {
    const current = this.userGuide();
    const file = this.selectedGuide();
    if (!current || !file) {
      this.guideError.set('Pilih file PDF panduan pengguna terlebih dahulu.');
      return;
    }
    if (file.type !== 'application/pdf' || !file.name.toLowerCase().endsWith('.pdf')) {
      this.guideError.set('Panduan pengguna wajib berupa file PDF.');
      return;
    }

    this.guideSaving.set(true);
    this.guideError.set('');
    this.guideSuccess.set('');
    this.api
      .replaceUserGuide(current, file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.userGuide.set(updated);
          this.selectedGuide.set(null);
          fileInput.value = '';
          this.guideSaving.set(false);
          this.guideSuccess.set('Panduan pengguna terbaru telah diterbitkan.');
        },
        error: (response) => {
          this.guideSaving.set(false);
          this.guideError.set(response?.error?.detail ?? 'Panduan pengguna gagal diunggah.');
          if (response?.status === 409) this.loadUserGuide();
        },
      });
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

  private loadUserGuide(): void {
    this.api
      .userGuide()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (setting) => {
          this.userGuide.set(setting);
          this.guideLoading.set(false);
        },
        error: (response) => {
          this.guideLoading.set(false);
          this.guideError.set(
            response?.error?.detail ?? 'Informasi panduan pengguna gagal dimuat.',
          );
        },
      });
  }
}
