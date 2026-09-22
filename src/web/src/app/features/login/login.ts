import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApplicationSettingsApi } from '../../core/application-settings-api';
import {
  IdentityApi,
  startDemoSession,
  startLocalLoginSession,
} from '../../core/development-identity';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly api = inject(IdentityApi);
  private readonly settingsApi = inject(ApplicationSettingsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);
  protected readonly submitting = signal(false);
  protected readonly error = signal('');
  protected readonly demoModeEnabled = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  constructor() {
    this.settingsApi.publicOptions().subscribe({
      next: (options) => this.demoModeEnabled.set(options.demoModeEnabled),
      error: () => this.demoModeEnabled.set(false),
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Masukkan username dan password.');
      return;
    }

    this.submitting.set(true);
    this.error.set('');
    this.api.login(this.form.getRawValue()).subscribe({
      next: () => {
        startLocalLoginSession();
        globalThis.location.assign(this.returnUrl());
      },
      error: (response) => {
        this.submitting.set(false);
        this.error.set(response?.error?.detail ?? 'Login gagal.');
      },
    });
  }

  protected useDemo(): void {
    if (!this.demoModeEnabled()) return;
    startDemoSession();
    globalThis.location.assign(this.returnUrl());
  }

  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl');
    return requested?.startsWith('/') && !requested.startsWith('//') ? requested : '/';
  }
}
