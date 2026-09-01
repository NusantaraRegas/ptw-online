import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  OperationalPolicyApi,
  OperationalPolicyReadiness,
  PolicySimulationRequest,
  PolicySimulationResult,
} from '../../core/operational-policy-api';

@Component({
  selector: 'app-admin-policy-readiness',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './admin-policy-readiness.html',
  styleUrl: './admin-policy-readiness.scss',
})
export class AdminPolicyReadiness {
  private readonly api = inject(OperationalPolicyApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly readiness = signal<OperationalPolicyReadiness | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly accessDenied = signal(false);
  protected readonly simulating = signal(false);
  protected readonly simulationError = signal('');
  protected readonly simulation = signal<PolicySimulationResult | null>(null);
  protected readonly completedCount = computed(
    () => this.readiness()?.requirements.filter((item) => item.satisfied).length ?? 0,
  );
  protected readonly simulationForm = this.formBuilder.nonNullable.group({
    subjectId: ['', [Validators.required, Validators.maxLength(200)]],
    actionCode: ['', [Validators.required, Validators.maxLength(100)]],
    locationCode: ['', [Validators.required, Validators.maxLength(100)]],
    competencyCodes: [''],
    evaluatedAt: [''],
  });

  constructor() {
    this.api
      .readiness()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (readiness) => {
          this.readiness.set(readiness);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.accessDenied.set(response?.status === 403);
          this.error.set(response?.error?.detail ?? 'Kesiapan policy gagal dimuat.');
        },
      });
  }

  protected simulate(): void {
    if (this.simulationForm.invalid) {
      this.simulationForm.markAllAsTouched();
      this.simulationError.set('Lengkapi subject ID, action code, dan kode lokasi.');
      return;
    }

    const value = this.simulationForm.getRawValue();
    const request: PolicySimulationRequest = {
      subjectId: value.subjectId.trim(),
      actionCode: value.actionCode.trim(),
      locationCode: value.locationCode.trim(),
      competencyCodes: this.splitCodes(value.competencyCodes),
      evaluatedAt: value.evaluatedAt ? new Date(value.evaluatedAt).toISOString() : null,
    };

    this.simulating.set(true);
    this.simulationError.set('');
    this.simulation.set(null);
    this.api
      .simulate(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.simulation.set(result);
          this.simulating.set(false);
        },
        error: (response) => {
          this.simulating.set(false);
          this.simulationError.set(response?.error?.detail ?? 'Simulasi policy gagal dijalankan.');
        },
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
