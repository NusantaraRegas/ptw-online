import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { DevelopmentIdentityStore } from '../../core/development-identity';
import { LocationApi, LocationOption } from '../../core/location-api';
import {
  Permit,
  PermitApi,
  PermitDraft,
  PermitTask,
  PermitWorkTypeOption,
} from '../../core/permit-api';
import { PermitAttachmentPermitChange, PermitAttachments } from './permit-attachments';
import { PermitHistory } from './permit-history';
import { PermitPrintPackages } from './permit-print-packages';
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
    PermitPrintPackages,
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
  protected readonly tasks = signal<PermitTask[]>([]);
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
  protected readonly workTypeCatalog = signal<Record<string, PermitWorkTypeOption[]>>({});
  protected readonly loadingWorkTypes = signal(true);
  protected readonly workTypeError = signal('');
  protected readonly roles = this.identityStore.selected().roles;
  protected readonly actorId = this.identityStore.selected().userId;
  protected readonly canEdit = computed(() => {
    const status = this.permit()?.status;
    return status === 'DRAFT' || status === 'REVISION_REQUIRED';
  });
  /** Ready print packages, used to bind a signed field copy to the sheet actually used in the field. */
  protected readonly readyPrintPackages = signal<{ id: string; permitVersion: number }[]>([]);

  protected readonly canRetryPrintPackage = computed(() => this.roles.includes('Administrator'));

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
      UNDER_VALIDATION: 'Menunggu validasi PIC HSE',
      AWAITING_AREA_APPROVAL: 'Menunggu approval penerbitan pemilik area',
      ISSUED: 'Diterbitkan — kontrol hardcopy tetap wajib',
      SUSPENDED: 'Ditangguhkan',
      CLOSURE_REQUESTED: 'Menunggu verifikasi penutupan',
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
  protected readonly currentTask = computed(() =>
    this.tasks().find((task) => task.permitId === this.permit()?.id),
  );
  protected readonly canValidateHse = computed(
    () =>
      this.currentTask()?.type === 'HSE_VALIDATION' &&
      this.roles.includes('HSEValidator') &&
      this.permit()?.draft.sponsorId !== this.actorId,
  );
  protected readonly canApprove = computed(
    () =>
      this.currentTask()?.type === 'AREA_APPROVE_AND_ISSUE' &&
      this.roles.includes('AreaOwnerManager'),
  );
  protected readonly canDisposition = computed(() => {
    const task = this.currentTask();
    return (
      !!task &&
      ((task.type === 'HSE_VALIDATION' && this.roles.includes('HSEValidator')) ||
        (task.type === 'AREA_APPROVE_AND_ISSUE' && this.roles.includes('AreaOwnerManager')))
    );
  });
  protected readonly canSuspend = computed(
    () =>
      this.permit()?.status === 'ISSUED' &&
      this.roles.some((role) =>
        ['HSEValidator', 'AreaOwnerManager', 'Administrator'].includes(role),
      ),
  );
  protected readonly canRequestRenewal = computed(
    () =>
      ['ISSUED', 'EXPIRED'].includes(this.permit()?.status ?? '') &&
      !this.permit()?.renewalPermitId &&
      this.roles.includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId,
  );
  protected readonly canResolveSuspension = computed(
    () =>
      this.permit()?.status === 'SUSPENDED' &&
      this.roles.some((role) => ['AreaOwnerManager', 'Administrator'].includes(role)),
  );
  protected readonly hasDecisionAction = computed(
    () =>
      this.canValidateHse() ||
      this.canApprove() ||
      this.canDisposition() ||
      this.canSuspend() ||
      this.canResolveSuspension(),
  );

  protected readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    description: ['', Validators.required],
    locationId: ['', Validators.required],
    performingAuthority: ['', Validators.required],
    company: ['', Validators.required],
    submitterType: this.fb.nonNullable.control<'CONTRACTOR' | 'USER_SPONSOR'>('USER_SPONSOR', {
      validators: [Validators.required],
    }),
    permitClass: ['HotWork', Validators.required],
    riskLevel: ['High', Validators.required],
    workTypeCodes: this.fb.nonNullable.control<string[]>([], {
      validators: [Validators.required],
    }),
    equipmentTag: [''],
    plantArea: ['', Validators.required],
    clsrApplicable: [false],
    simopsDeclaration: [''],
    safetyEquipmentCodes: [''],
    isolationPrecautionCodes: [''],
    jsaDocumentNumber: ['', Validators.required],
    jsaRevision: ['', Validators.required],
    jsaDate: ['', Validators.required],
    validFrom: ['', Validators.required],
    validUntil: ['', Validators.required],
    eSimiNumber: [''],
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
  protected readonly renewalForm = this.fb.nonNullable.group({
    validFrom: ['', Validators.required],
    validUntil: ['', Validators.required],
  });

  constructor() {
    this.loadLocations();
    this.loadWorkTypes();
    this.form.controls.permitClass.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reconcileWorkTypeSelection());
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
      submitterType: permit.draft.submitterType ?? 'USER_SPONSOR',
      permitClass: permit.draft.permitClass,
      riskLevel: permit.draft.riskLevel,
      workTypeCodes:
        permit.draft.workTypeCodes ??
        (permit.draft.workTypeCode ? [permit.draft.workTypeCode] : []),
      equipmentTag: permit.draft.equipmentTag ?? '',
      plantArea: permit.draft.plantArea ?? '',
      clsrApplicable: permit.draft.clsrApplicable ?? false,
      simopsDeclaration: permit.draft.simopsDeclaration ?? '',
      safetyEquipmentCodes: (permit.draft.safetyEquipmentCodes ?? []).join(', '),
      isolationPrecautionCodes: (permit.draft.isolationPrecautionCodes ?? []).join(', '),
      jsaDocumentNumber: permit.draft.jsaDocumentNumber ?? '',
      jsaRevision: permit.draft.jsaRevision ?? '',
      jsaDate: permit.draft.jsaDate?.slice(0, 10) ?? '',
      validFrom: toLocalInput(permit.draft.validFrom),
      validUntil: toLocalInput(permit.draft.validUntil),
      eSimiNumber: permit.draft.eSimiNumber ?? '',
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
      hazards: [],
      controls: [],
      requiredDocumentCodes: permit.draft.requiredDocumentCodes,
      workTypeCode: null,
      equipmentTag: value.equipmentTag || null,
      plantArea: value.plantArea || null,
      simopsDeclaration: value.simopsDeclaration || null,
      safetyEquipmentCodes: this.split(value.safetyEquipmentCodes),
      isolationPrecautionCodes: this.split(value.isolationPrecautionCodes),
      jsaDocumentNumber: value.jsaDocumentNumber || null,
      jsaRevision: value.jsaRevision || null,
      jsaDate: value.jsaDate ? new Date(`${value.jsaDate}T00:00:00`).toISOString() : null,
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

  protected workTypeOptions(): PermitWorkTypeOption[] {
    return this.workTypeCatalog()[this.form.controls.permitClass.value] ?? [];
  }

  protected isWorkTypeSelected(code: string): boolean {
    return this.form.controls.workTypeCodes.value.includes(code);
  }

  protected toggleWorkType(code: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.form.controls.workTypeCodes.value;
    const next = checked
      ? [...new Set([...current, code])]
      : current.filter((item) => item !== code);
    this.form.controls.workTypeCodes.setValue(next);
    this.form.controls.workTypeCodes.markAsTouched();
  }

  protected workTypeLabels(draft: PermitDraft): string {
    const codes = draft.workTypeCodes ?? (draft.workTypeCode ? [draft.workTypeCode] : []);
    const options = this.workTypeCatalog()[draft.permitClass] ?? [];
    return (
      codes
        .map((code) => options.find((option) => option.code === code)?.label ?? code)
        .join(', ') || 'Belum diisi'
    );
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
      'PTW diajukan untuk validasi PIC HSE.',
    );
  }

  protected validateHse(): void {
    this.runTaskDecision((task, permit, statement) =>
      this.api.validate(task.id, permit.eTag, statement),
    );
  }

  protected approveAndIssue(): void {
    this.runTaskDecision(
      (task, permit, statement) =>
        this.api.approveAndIssue(task.id, permit.eTag, {
          statement,
          actingAssignmentId: null,
        }),
      'PTW berhasil diterbitkan. Gas test, readiness, revalidasi, dan tanda tangan hardcopy tetap wajib sebelum kerja.',
    );
  }

  protected requestRevision(): void {
    this.runTaskDecision(
      (task, permit, reason) => this.api.requestRevision(task.id, permit.eTag, reason),
      'PTW dikembalikan kepada Sponsor untuk revisi. Seluruh validasi aktif harus diulang.',
    );
  }

  protected reject(): void {
    if (!globalThis.confirm('Tolak PTW ini secara permanen? Aksi ini tidak dapat dibatalkan.')) {
      return;
    }
    this.runTaskDecision(
      (task, permit, reason) => this.api.reject(task.id, permit.eTag, reason),
      'PTW ditolak dan seluruh task aktif telah ditutup.',
    );
  }

  protected suspend(): void {
    if (!globalThis.confirm('Tangguhkan PTW? Pekerjaan harus dihentikan seketika.')) return;
    this.runDecision(
      (permit, reason) => this.api.suspend(permit.id, permit.eTag, reason),
      'PTW ditangguhkan. Pekerjaan harus berhenti dan hardcopy harus ditandai sesuai SOP.',
    );
  }

  protected resolveSuspension(): void {
    this.runDecision(
      (permit, statement) => this.api.resolveSuspension(permit.id, permit.eTag, statement),
      'Penangguhan diselesaikan. Revalidasi hardcopy tetap wajib sebelum pekerjaan dilanjutkan.',
    );
  }

  private runTaskDecision(
    command: (task: PermitTask, permit: Permit, statement: string) => Observable<Permit>,
    successMessage = 'Keputusan tersimpan.',
  ): void {
    const task = this.currentTask();
    const permit = this.permit();
    if (!task || !permit || this.decisionStatement.invalid) {
      this.decisionStatement.markAsTouched();
      return;
    }
    this.runCommand(command(task, permit, this.decisionStatement.getRawValue()), successMessage);
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
        this.refreshTasks();
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
          this.refreshTasks();
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

  private refreshTasks(): void {
    this.api
      .listTasks()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => this.tasks.set(page.items),
        error: () => this.tasks.set([]),
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

  private loadWorkTypes(): void {
    this.api
      .listWorkTypes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.workTypeCatalog.set(
            Object.fromEntries(catalog.map((item) => [item.permitClass, item.options])),
          );
          this.loadingWorkTypes.set(false);
          this.reconcileWorkTypeSelection();
        },
        error: (response) => {
          this.loadingWorkTypes.set(false);
          this.workTypeError.set(
            response?.error?.detail ??
              'Daftar jenis pekerjaan gagal dimuat. Coba muat ulang halaman.',
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

  private reconcileWorkTypeSelection(): void {
    const validCodes = new Set(this.workTypeOptions().map((option) => option.code));
    const current = this.form.controls.workTypeCodes.value;
    const next = current.filter((code) => validCodes.has(code));
    if (next.length !== current.length) {
      this.form.controls.workTypeCodes.setValue(next);
    }
  }
}
