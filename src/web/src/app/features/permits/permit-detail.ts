import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { DevelopmentIdentityStore } from '../../core/development-identity';
import { LocationApi, LocationOption } from '../../core/location-api';
import { IssuePermitRequest, Permit, PermitApi, PermitDraft } from '../../core/permit-api';
import { PermitAttachmentPermitChange, PermitAttachments } from './permit-attachments';
import { PermitHistory } from './permit-history';
import { PermitValidationProgress } from './permit-validation-progress';

function toLocalInput(value: string): string {
  const date = new Date(value);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

@Component({
  selector: 'app-permit-detail',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    PermitAttachments,
    PermitHistory,
    PermitValidationProgress,
  ],
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
  private permitId = '';

  protected readonly permit = signal<Permit | null>(null);
  protected readonly loading = signal(true);
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly conflict = signal(false);
  protected readonly showingRenewalForm = signal(false);
  protected readonly renewalError = signal('');
  protected readonly renewalConflict = signal(false);
  protected readonly renewalCreatedId = signal<string | null>(null);
  protected readonly locations = signal<LocationOption[]>([]);
  protected readonly loadingLocations = signal(true);
  protected readonly locationError = signal('');
  protected readonly roles = this.identityStore.selected().roles;
  protected readonly actorId = this.identityStore.selected().userId;
  protected readonly canEdit = computed(() => {
    const status = this.permit()?.status;
    return status === 'DRAFT' || status === 'REVISION_REQUIRED';
  });
  protected readonly canManageAttachments = computed(
    () =>
      this.canEdit() &&
      (this.roles.includes('Administrator') ||
        (this.roles.includes('Sponsor') && this.permit()?.draft.sponsorId === this.actorId)),
  );
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
      SUSPENSION_REQUESTED: 'Permintaan penangguhan — pekerjaan dihentikan',
      SUSPENDED: 'Ditangguhkan',
      COMPLETION_CONFIRMATION_PENDING: 'Menunggu konfirmasi penyelesaian',
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
  protected readonly canApprove = computed(
    () => this.permit()?.status === 'AWAITING_APPROVAL' && this.roles.includes('AreaOwnerApprover'),
  );
  protected readonly canIssue = computed(
    () =>
      ['APPROVED', 'READY_FOR_ISSUE'].includes(this.permit()?.status ?? '') &&
      this.roles.includes('AreaOwnerApprover') &&
      this.permit()?.workflow.approvedBy === this.actorId,
  );
  protected readonly canDisposition = computed(() => {
    const status = this.permit()?.status;
    return (
      (status === 'UNDER_REVIEW' && this.roles.includes('HSSEValidator')) ||
      (status === 'AWAITING_APPROVAL' && this.roles.includes('AreaOwnerApprover'))
    );
  });
  protected readonly canRequestSuspension = computed(
    () =>
      this.permit()?.status === 'OPEN' &&
      this.roles.includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId,
  );
  protected readonly canRequestRenewal = computed(
    () =>
      this.permit()?.status === 'OPEN' &&
      !this.permit()?.renewalPermitId &&
      this.roles.includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId,
  );
  protected readonly canApproveSuspension = computed(
    () =>
      this.permit()?.status === 'SUSPENSION_REQUESTED' && this.roles.includes('AreaOwnerApprover'),
  );
  protected readonly canDeclareCompletion = computed(
    () =>
      this.permit()?.status === 'OPEN' &&
      this.roles.includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId,
  );
  protected readonly canConfirmHsseCompletion = computed(
    () =>
      this.permit()?.status === 'COMPLETION_CONFIRMATION_PENDING' &&
      this.roles.includes('HSSEValidator') &&
      !this.permit()?.workflow.completion.hsse.completed,
  );
  protected readonly canConfirmAreaOwnerCompletion = computed(
    () =>
      this.permit()?.status === 'COMPLETION_CONFIRMATION_PENDING' &&
      this.roles.includes('AreaOwnerApprover') &&
      !this.permit()?.workflow.completion.areaOwner.completed,
  );
  protected readonly canClose = computed(
    () => this.permit()?.status === 'WORK_COMPLETED' && this.roles.includes('AreaOwnerApprover'),
  );
  protected readonly hasDecisionAction = computed(
    () =>
      this.canValidateHsse() ||
      this.canApprove() ||
      this.canDisposition() ||
      this.canRequestSuspension() ||
      this.canApproveSuspension() ||
      this.canDeclareCompletion() ||
      this.canConfirmHsseCompletion() ||
      this.canConfirmAreaOwnerCompletion() ||
      this.canClose(),
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
  protected readonly renewalForm = this.fb.nonNullable.group({
    validFrom: ['', Validators.required],
    validUntil: ['', Validators.required],
  });

  constructor() {
    this.loadLocations();
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.permitId = params.get('id') ?? '';
      this.editing.set(false);
      this.showingRenewalForm.set(false);
      this.renewalError.set('');
      this.renewalConflict.set(false);
      this.renewalCreatedId.set(null);
      this.permit.set(null);

      if (!this.permitId) {
        this.loading.set(false);
        this.error.set('PTW tidak ditemukan atau tidak lagi tersedia.');
        return;
      }

      this.load();
    });
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
    this.showingRenewalForm.set(false);
    this.renewalError.set('');
    this.renewalConflict.set(false);
    this.load();
  }

  protected applyAttachmentPermitChange(change: PermitAttachmentPermitChange): void {
    this.permit.update((permit) =>
      permit ? { ...permit, eTag: change.eTag, version: change.version } : permit,
    );
  }

  protected openRenewalForm(): void {
    const permit = this.permit();
    if (!permit || !this.canRequestRenewal()) return;
    this.renewalForm.reset({
      validFrom: toLocalInput(permit.draft.validUntil),
      validUntil: '',
    });
    this.error.set('');
    this.success.set('');
    this.renewalError.set('');
    this.renewalConflict.set(false);
    this.showingRenewalForm.set(true);
  }

  protected cancelRenewal(): void {
    this.showingRenewalForm.set(false);
    this.renewalError.set('');
    this.renewalConflict.set(false);
    this.renewalForm.reset();
  }

  protected requestRenewal(): void {
    const permit = this.permit();
    if (!permit || this.renewalForm.invalid) {
      this.renewalForm.markAllAsTouched();
      return;
    }

    const value = this.renewalForm.getRawValue();
    this.saving.set(true);
    this.success.set('');
    this.renewalError.set('');
    this.renewalConflict.set(false);
    this.api
      .requestRenewal(permit.id, permit.eTag, {
        validFrom: new Date(value.validFrom).toISOString(),
        validUntil: new Date(value.validUntil).toISOString(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.permit.update((current) =>
            current
              ? {
                  ...current,
                  version: result.sourcePermitVersion,
                  eTag: result.sourceETag,
                  renewalPermitId: result.renewal.id,
                }
              : current,
          );
          this.renewalCreatedId.set(result.renewal.id);
          this.showingRenewalForm.set(false);
          this.saving.set(false);
          this.renewalError.set('');
          this.renewalConflict.set(false);
          this.success.set('Draft renewal berhasil dibuat dengan nomor PTW baru saat diajukan.');
        },
        error: (response) => {
          this.saving.set(false);
          this.renewalConflict.set(response.status === 409);
          this.renewalError.set(response?.error?.detail ?? 'Pengajuan renewal gagal diproses.');
        },
      });
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
      'PTW diajukan untuk validasi HSSE.',
    );
  }

  protected endorseHsse(): void {
    this.runDecision((permit, statement) =>
      this.api.endorseHsse(permit.id, permit.eTag, statement),
    );
  }

  protected approve(): void {
    this.runDecision((permit, statement) => this.api.approve(permit.id, permit.eTag, statement));
  }

  protected requestRevision(): void {
    this.runDecision(
      (permit, reason) => this.api.requestRevision(permit.id, permit.eTag, reason),
      'PTW dikembalikan kepada Sponsor untuk revisi. Seluruh validasi aktif harus diulang.',
    );
  }

  protected reject(): void {
    if (!globalThis.confirm('Tolak PTW ini secara permanen? Aksi ini tidak dapat dibatalkan.')) {
      return;
    }
    this.runDecision(
      (permit, reason) => this.api.reject(permit.id, permit.eTag, reason),
      'PTW ditolak dan seluruh task aktif telah ditutup.',
    );
  }

  protected requestSuspension(): void {
    if (!globalThis.confirm('Ajukan penangguhan? Hak kerja akan dihentikan seketika.')) return;
    this.runDecision(
      (permit, reason) => this.api.requestSuspension(permit.id, permit.eTag, reason),
      'Pekerjaan langsung dihentikan. Persetujuan penangguhan menunggu PIC pemilik area.',
    );
  }

  protected approveSuspension(): void {
    this.runDecision(
      (permit, statement) => this.api.approveSuspension(permit.id, permit.eTag, statement),
      'Penangguhan disetujui oleh PIC pemilik area.',
    );
  }

  protected declareCompletion(): void {
    if (!globalThis.confirm('Nyatakan pekerjaan selesai dan hentikan active work period?')) return;
    this.runDecision(
      (permit, statement) => this.api.declareCompletion(permit.id, permit.eTag, statement),
      'Pekerjaan dinyatakan selesai. Konfirmasi HSSE dan PIC pemilik area telah diminta.',
    );
  }

  protected confirmHsseCompletion(): void {
    this.runDecision(
      (permit, statement) => this.api.confirmHsseCompletion(permit.id, permit.eTag, statement),
      'Konfirmasi penyelesaian HSSE tersimpan.',
    );
  }

  protected confirmAreaOwnerCompletion(): void {
    this.runDecision(
      (permit, statement) => this.api.confirmAreaOwnerCompletion(permit.id, permit.eTag, statement),
      'Konfirmasi penyelesaian PIC pemilik area tersimpan.',
    );
  }

  protected closePermit(): void {
    if (!globalThis.confirm('Tutup PTW ini secara permanen?')) return;
    this.runDecision(
      (permit, statement) => this.api.close(permit.id, permit.eTag, statement),
      'PTW berhasil ditutup.',
    );
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

  private runDecision(
    command: (permit: Permit, statement: string) => Observable<Permit>,
    successMessage = 'Keputusan tersimpan.',
  ): void {
    const permit = this.permit();
    if (!permit || this.decisionStatement.invalid) {
      this.decisionStatement.markAsTouched();
      return;
    }
    this.runCommand(command(permit, this.decisionStatement.getRawValue()), successMessage);
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
