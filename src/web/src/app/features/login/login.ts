import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
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
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);
  protected readonly submitting = signal(false);
  protected readonly error = signal('');
  protected readonly form = this.formBuilder.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

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
    startDemoSession();
    globalThis.location.assign(this.returnUrl());
  }

  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl');
    return requested?.startsWith('/') && !requested.startsWith('//') ? requested : '/';
  }
}
