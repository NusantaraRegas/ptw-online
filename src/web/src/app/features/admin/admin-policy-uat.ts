import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  PolicyUatApi,
  PolicyUatRun,
  PolicyUatScenario,
  PolicyUatSuite,
} from '../../core/policy-uat-api';

@Component({
  selector: 'app-admin-policy-uat',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-policy-uat.html',
  styleUrl: './admin-policy-uat.scss',
})
export class AdminPolicyUat {
  private readonly api = inject(PolicyUatApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly suites = signal<PolicyUatSuite[]>([]);
  protected readonly latestRun = signal<PolicyUatRun | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly runningSuiteId = signal('');
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly accessDenied = signal(false);
  protected readonly suiteForm = this.formBuilder.nonNullable.group({
    suiteKey: ['OPN-002-BASELINE', [Validators.required, Validators.maxLength(100)]],
    name: ['Baseline authorization policy', [Validators.required, Validators.maxLength(200)]],
    policyVersion: ['', [Validators.required, Validators.maxLength(100)]],
    scenarios: this.formBuilder.array([this.createScenario()]),
  });

  constructor() {
    this.loadSuites();
  }

  protected get scenarios() {
    return this.suiteForm.controls.scenarios;
  }

  protected addScenario(): void {
    if (this.scenarios.length < 200) {
      this.scenarios.push(this.createScenario());
    }
  }

  protected removeScenario(index: number): void {
    if (this.scenarios.length > 1) {
      this.scenarios.removeAt(index);
    }
  }

  protected saveSuite(): void {
    if (this.suiteForm.invalid) {
      this.suiteForm.markAllAsTouched();
      this.error.set('Lengkapi metadata dan seluruh field wajib pada setiap skenario.');
      return;
    }

    const value = this.suiteForm.getRawValue();
    const scenarios: PolicyUatScenario[] = value.scenarios.map((scenario) => ({
      caseCode: scenario.caseCode.trim(),
      description: scenario.description.trim(),
      subjectId: scenario.subjectId.trim(),
      actionCode: scenario.actionCode.trim(),
      locationCode: scenario.locationCode.trim(),
      competencyCodes: this.splitCodes(scenario.competencyCodes),
      evaluatedAt: scenario.evaluatedAt ? new Date(scenario.evaluatedAt).toISOString() : null,
      expectedOutcome: scenario.expectedOutcome,
      expectedCode: scenario.expectedCode.trim() || null,
    }));

    this.saving.set(true);
    this.error.set('');
    this.success.set('');
    this.api
      .createSuite({
        suiteKey: value.suiteKey.trim(),
        name: value.name.trim(),
        policyVersion: value.policyVersion.trim(),
        scenarios,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (suite) => {
          this.saving.set(false);
          this.success.set(
            `Paket ${suite.suiteKey} versi ${suite.version} disimpan secara immutable.`,
          );
          this.loadSuites(false);
        },
        error: (response) => {
          this.saving.set(false);
          this.error.set(response?.error?.detail ?? 'Paket UAT gagal disimpan.');
        },
      });
  }

  protected runSuite(suite: PolicyUatSuite): void {
    this.runningSuiteId.set(suite.id);
    this.error.set('');
    this.success.set('');
    this.latestRun.set(null);
    this.api
      .runSuite(suite.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (run) => {
          this.runningSuiteId.set('');
          this.latestRun.set(run);
          this.success.set(
            run.passed
              ? 'Seluruh expected outcome cocok; bukti run dapat dipakai oleh activation gate.'
              : 'Run selesai, tetapi mismatch masih memblokir activation gate.',
          );
          this.loadSuites(false);
        },
        error: (response) => {
          this.runningSuiteId.set('');
          this.error.set(response?.error?.detail ?? 'Batch UAT gagal dijalankan.');
        },
      });
  }

  private loadSuites(showLoading = true): void {
    if (showLoading) {
      this.loading.set(true);
    }
    this.api
      .listSuites()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.suites.set(response.items);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.accessDenied.set(response?.status === 403);
          this.error.set(response?.error?.detail ?? 'Daftar paket UAT gagal dimuat.');
        },
      });
  }

  private createScenario() {
    return this.formBuilder.nonNullable.group({
      caseCode: ['', [Validators.required, Validators.maxLength(100)]],
      description: ['', [Validators.required, Validators.maxLength(500)]],
      subjectId: ['', [Validators.required, Validators.maxLength(200)]],
      actionCode: ['', [Validators.required, Validators.maxLength(100)]],
      locationCode: ['', [Validators.required, Validators.maxLength(100)]],
      competencyCodes: [''],
      evaluatedAt: [''],
      expectedOutcome: this.formBuilder.nonNullable.control<'ALLOW' | 'DENY'>('ALLOW'),
      expectedCode: ['', Validators.maxLength(100)],
    });
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
}
