import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { DevelopmentIdentityStore } from '../../core/development-identity';
import { LocationApi, LocationOption } from '../../core/location-api';
import { IssuePermitRequest, Permit, PermitApi, PermitDraft } from '../../core/permit-api';
import { PermitHistory } from './permit-history';
import { PermitValidationProgress } from './permit-validation-progress';

function toLocalInput(value: string): string {
  const date = new Date(value);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

@Component({
  selector: 'app-permit-detail',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, PermitHistory, PermitValidationProgress],
  templateUrl: './permit-detail.html',
  styleUrl: './permit-detail.scss',
})
export class PermitDetail {
  private readonly api = inject(PermitApi);
  private readonly locationApi = inject(LocationApi);
  private readonly identityStore = inject(DevelopmentIdentityStore);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private readonly permitId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly permit = signal<Permit | null>(null);
  protected readonly loading = signal(true);
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly conflict = signal(false);
  protected readonly locations = signal<LocationOption[]>([]);
  protected readonly loadingLocations = signal(true);
  protected readonly locationError = signal('');
  protected readonly roles = this.identityStore.selected().roles;
  protected readonly canEdit = computed(() => {
    const status = this.permit()?.status;
    return status === 'DRAFT' || status === 'REVISION_REQUIRED';
  });
  protected readonly statusLabel = computed(() => {
    const labels: Record<string, string> = {
      DRAFT: 'Draft',
      REVISION_REQUIRED: 'Perlu revisi',
      SUBMITTED: 'Diajukan',
      UNDER_REVIEW: 'Sedang ditinjau',
      AWAITING_APPROVAL: 'Menunggu persetujuan',
      APPROVED: 'Disetujui — belum boleh bekerja',
      READY_FOR_ISSUE: 'Siap diterbitkan',
      OPEN: 'Diterbitkan — pekerjaan aktif',
      SUSPENDED: 'Ditangguhkan',
      WORK_COMPLETED: 'Pekerjaan selesai',
      CLOSED: 'Ditutup',
      REJECTED: 'Ditolak',
      CANCELLED: 'Dibatalkan',
      EXPIRED: 'Kedaluwarsa',
    };
    const status = this.permit()?.status ?? '';
    return labels[status] ?? status;
  });
  protected readonly canSubmit = computed(
    () =>
      this.canEdit() &&
      (this.roles.includes('Sponsor') || this.roles.includes('Administrator')) &&
      !this.editing(),
  );
  protected readonly canValidateHsse = computed(
    () =>
      this.permit()?.status === 'UNDER_REVIEW' &&
      this.roles.includes('HSSEValidator') &&
      !this.permit()?.workflow.hsse.completed,
  );
  protected readonly canValidateGas = computed(
    () =>
      this.permit()?.status === 'UNDER_REVIEW' &&
      this.roles.includes('GasDistributionValidator') &&
      !this.permit()?.workflow.gasDistribution.completed,
  );
  protected readonly canApprove = computed(
    () => this.permit()?.status === 'AWAITING_APPROVAL' && this.roles.includes('AreaOwnerApprover'),
  );
  protected readonly canIssue = computed(
    () =>
      ['APPROVED', 'READY_FOR_ISSUE'].includes(this.permit()?.status ?? '') &&
      this.roles.includes('IssuingAuthority'),
  );
  protected readonly hasDecisionAction = computed(
    () => this.canValidateHsse() || this.canValidateGas() || this.canApprove(),
  );

  protected readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    description: ['', Validators.required],
    locationId: ['', Validators.required],
    performingAuthority: ['', Validators.required],
    company: ['', Validators.required],
    permitClass: ['HotWork', Validators.required],
    riskLevel: ['High', Validators.required],
    validFrom: ['', Validators.required],
    validUntil: ['', Validators.required],
    eSimiNumber: [''],
    hazards: ['', Validators.required],
    controls: ['', Validators.required],
  });
  protected readonly submissionForm = this.fb.nonNullable.group({
    eSimiEligible: [false, Validators.requiredTrue],
    rulesEvaluated: [false, Validators.requiredTrue],
    requiredDocumentsSafe: [false, Validators.requiredTrue],
    noMissingRequirements: [false, Validators.requiredTrue],
  });
  protected readonly decisionStatement = this.fb.nonNullable.control('', [
    Validators.required,
    Validators.maxLength(1000),
  ]);
  protected readonly issueForm = this.fb.nonNullable.group({
    eSimiEligible: [false, Validators.requiredTrue],
    locationVerified: [false, Validators.requiredTrue],
    toolboxTalkComplete: [false, Validators.requiredTrue],
    personnelAcknowledged: [false, Validators.requiredTrue],
    ppeAndControlsVerified: [false, Validators.requiredTrue],
    isolationVerified: [false, Validators.requiredTrue],
    simopsVerified: [false, Validators.requiredTrue],
    gasTestSatisfied: [false, Validators.requiredTrue],
    noUnresolvedSuspension: [false, Validators.requiredTrue],
  });

  constructor() {
    this.loadLocations();
    this.load();
  }

  protected startEdit(): void {
    const permit = this.permit();
    if (!permit || !this.canEdit()) return;
    this.form.reset({
      title: permit.draft.title,
      description: permit.draft.description,
      locationId: permit.draft.locationId,
      performingAuthority: permit.draft.performingAuthority,
      company: permit.draft.company,
      permitClass: permit.draft.permitClass,
      riskLevel: permit.draft.riskLevel,
      validFrom: toLocalInput(permit.draft.validFrom),
      validUntil: toLocalInput(permit.draft.validUntil),
      eSimiNumber: permit.draft.eSimiNumber ?? '',
      hazards: permit.draft.hazards.join(', '),
      controls: permit.draft.controls.join(', '),
    });
    this.error.set('');
    this.success.set('');
    this.conflict.set(false);
    this.editing.set(true);
  }

  protected cancelEdit(): void {
    this.editing.set(false);
    this.error.set('');
    this.conflict.set(false);
  }

  protected save(): void {
    const permit = this.permit();
    if (!permit || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const draft: PermitDraft = {
      ...value,
      sponsorId: permit.draft.sponsorId,
      validFrom: new Date(value.validFrom).toISOString(),
      validUntil: new Date(value.validUntil).toISOString(),
      eSimiExternalId: value.eSimiNumber || null,
      eSimiNumber: value.eSimiNumber || null,
      hazards: this.split(value.hazards),
      controls: this.split(value.controls),
      requiredDocumentCodes: permit.draft.requiredDocumentCodes,
    };

    this.saving.set(true);
    this.error.set('');
    this.success.set('');
    this.conflict.set(false);
    this.api
      .updateDraft(permit.id, draft, permit.eTag)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.permit.set(updated);
          this.saving.set(false);
          this.editing.set(false);
          this.success.set('Perubahan draft berhasil disimpan.');
        },
        error: (response) => {
          this.saving.set(false);
          this.conflict.set(response.status === 409);
          this.error.set(
            response?.error?.detail ??
              'Draft gagal disimpan. Periksa koneksi API dan data formulir.',
          );
        },
      });
  }

  protected reload(): void {
    this.editing.set(false);
    this.load();
  }

  protected submitForValidation(): void {
    const permit = this.permit();
    if (!permit || this.submissionForm.invalid) {
      this.submissionForm.markAllAsTouched();
      return;
    }
    const value = this.submissionForm.getRawValue();
    this.runCommand(
      this.api.submit(permit.id, permit.eTag, {
        eSimiEligible: value.eSimiEligible,
        rulesEvaluated: value.rulesEvaluated,
        requiredDocumentsSafe: value.requiredDocumentsSafe,
        missingRequirements: value.noMissingRequirements ? [] : ['Persyaratan belum lengkap'],
      }),
      'PTW diajukan untuk validasi paralel HSSE dan Distribusi Gas & Pengelolaan ORF.',
    );
  }

  protected endorseHsse(): void {
    this.runDecision((permit, statement) =>
      this.api.endorseHsse(permit.id, permit.eTag, statement),
    );
  }

  protected endorseGas(): void {
    this.runDecision((permit, statement) =>
      this.api.endorseGasDistribution(permit.id, permit.eTag, statement),
    );
  }

  protected approve(): void {
    this.runDecision((permit, statement) => this.api.approve(permit.id, permit.eTag, statement));
  }

  protected issue(): void {
    const permit = this.permit();
    if (!permit || this.issueForm.invalid) {
      this.issueForm.markAllAsTouched();
      return;
    }
    const value = this.issueForm.getRawValue();
    const request: IssuePermitRequest = {
      eSimiEligible: value.eSimiEligible,
      locationVerified: value.locationVerified,
      toolboxTalkComplete: value.toolboxTalkComplete,
      personnelAcknowledged: value.personnelAcknowledged,
      ppeAndControlsVerified: value.ppeAndControlsVerified,
      isolationVerified: value.isolationVerified,
      simopsVerified: value.simopsVerified,
      gasTestSatisfied: value.gasTestSatisfied,
      hasUnresolvedSuspension: !value.noUnresolvedSuspension,
    };
    this.runCommand(
      this.api.issue(permit.id, permit.eTag, request),
      'PTW berhasil diterbitkan. Pekerjaan dapat berjalan dalam active work period.',
    );
  }

  private runDecision(command: (permit: Permit, statement: string) => Observable<Permit>): void {
    const permit = this.permit();
    if (!permit || this.decisionStatement.invalid) {
      this.decisionStatement.markAsTouched();
      return;
    }
    this.runCommand(command(permit, this.decisionStatement.getRawValue()), 'Keputusan tersimpan.');
  }

  private runCommand(command: Observable<Permit>, message: string): void {
    this.saving.set(true);
    this.error.set('');
    this.success.set('');
    command.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.permit.set(updated);
        this.saving.set(false);
        this.success.set(message);
        this.decisionStatement.reset();
      },
      error: (response) => {
        this.saving.set(false);
        this.conflict.set(response.status === 409);
        this.error.set(response?.error?.detail ?? 'Aksi workflow gagal diproses.');
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set('');
    this.success.set('');
    this.conflict.set(false);
    this.api
      .get(this.permitId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (permit) => {
          this.permit.set(permit);
          this.loading.set(false);
        },
        error: (response) => {
          this.loading.set(false);
          this.error.set(
            response.status === 404
              ? 'PTW tidak ditemukan atau tidak lagi tersedia.'
              : (response?.error?.detail ?? 'Detail PTW gagal dimuat.'),
          );
        },
      });
  }

  private loadLocations(): void {
    this.locationApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.locations.set(page.items);
          this.loadingLocations.set(false);
        },
        error: (response) => {
          this.loadingLocations.set(false);
          this.locationError.set(
            response?.error?.detail ?? 'Daftar lokasi gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });
  }

  private split(value: string): string[] {
    return value
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);
  }
}
