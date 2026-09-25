import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { CurrentIdentity, IdentityApi } from '../../core/development-identity';
import { LocationApi, LocationOption } from '../../core/location-api';
import { PermitAttachment, PermitAttachmentApi } from '../../core/permit-attachment-api';
import {
  Permit,
  PermitApi,
  PermitDraft,
  PermitHeaderClassificationCatalog,
  PermitHeaderClassificationOption,
  PermitMandatoryDocumentOption,
  PermitOperationalConditionOption,
  PermitSafetyEquipmentOption,
  PermitSupportingDocumentOption,
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
  private readonly attachmentApi = inject(PermitAttachmentApi);
  private readonly identityApi = inject(IdentityApi);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private permitId = '';

  protected readonly permit = signal<Permit | null>(null);
  protected readonly tasks = signal<PermitTask[]>([]);
  protected readonly identity = signal<CurrentIdentity | null>(null);
  protected readonly identityError = signal('');
  protected readonly taskError = signal('');
  protected readonly loading = signal(true);
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly conflict = signal(false);
  protected readonly showingRenewalForm = signal(false);
  protected readonly showingClosureForm = signal(false);
  protected readonly renewalError = signal('');
  protected readonly renewalConflict = signal(false);
  protected readonly closureError = signal('');
  protected readonly closureConflict = signal(false);
  protected readonly renewalCreatedId = signal<string | null>(null);
  protected readonly signedFieldCopies = signal<PermitAttachment[]>([]);
  protected readonly locations = signal<LocationOption[]>([]);
  protected readonly loadingLocations = signal(true);
  protected readonly locationError = signal('');
  protected readonly workTypeCatalog = signal<Record<string, PermitWorkTypeOption[]>>({});
  protected readonly loadingWorkTypes = signal(true);
  protected readonly workTypeError = signal('');
  protected readonly headerClassificationCatalog = signal<
    Record<string, PermitHeaderClassificationCatalog>
  >({});
  protected readonly loadingHeaderClassifications = signal(true);
  protected readonly headerClassificationError = signal('');
  protected readonly safetyEquipmentCatalog = signal<Record<string, PermitSafetyEquipmentOption[]>>(
    {},
  );
  protected readonly loadingSafetyEquipment = signal(true);
  protected readonly safetyEquipmentError = signal('');
  protected readonly supportingDocumentOptions = signal<PermitSupportingDocumentOption[]>([]);
  protected readonly loadingSupportingDocuments = signal(true);
  protected readonly supportingDocumentError = signal('');
  protected readonly mandatoryDocumentOptions = signal<PermitMandatoryDocumentOption[]>([]);
  protected readonly loadingMandatoryDocuments = signal(true);
  protected readonly mandatoryDocumentError = signal('');
  protected readonly operationalConditionOptions = signal<PermitOperationalConditionOption[]>([]);
  protected readonly loadingOperationalConditions = signal(true);
  protected readonly operationalConditionError = signal('');
  protected readonly roles = computed(() => this.identity()?.roles ?? []);
  protected readonly actorId = computed(() => this.identity()?.userId ?? '');
  protected readonly actorDisplayName = computed(() => this.identity()?.displayName ?? '');
  protected readonly canEdit = computed(() => {
    const status = this.permit()?.status;
    return status === 'DRAFT' || status === 'REVISION_REQUIRED';
  });
  /** Ready print packages, used to bind a signed field copy to the sheet actually used in the field. */
  protected readonly readyPrintPackages = signal<{ id: string; permitVersion: number }[]>([]);

  protected readonly canRetryPrintPackage = computed(() => this.roles().includes('Administrator'));

  protected locationName(code: string): string {
    return (
      this.locations().find((location) => location.code === code)?.name ??
      (this.loadingLocations() ? 'Memuat lokasi...' : 'Lokasi tidak tersedia')
    );
  }

  protected readonly canManageDraftAttachments = computed(
    () =>
      this.canEdit() &&
      (this.roles().includes('Administrator') ||
        (this.roles().includes('Sponsor') && this.permit()?.draft.sponsorId === this.actorId())),
  );
  protected readonly closureReplacementPending = computed(
    () =>
      this.permit()?.status === 'CLOSURE_REQUESTED' &&
      !!this.permit()?.workflow.closure.replacementReason,
  );
  protected readonly canResubmitClosure = computed(
    () =>
      this.closureReplacementPending() &&
      this.roles().includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId(),
  );
  protected readonly canUploadFieldCopy = computed(
    () =>
      (['ISSUED', 'SUSPENDED', 'EXPIRED'].includes(this.permit()?.status ?? '') ||
        this.closureReplacementPending()) &&
      this.permit()?.workflow.renewal?.status !== 'PENDING' &&
      (this.roles().includes('Administrator') ||
        (this.roles().includes('Sponsor') && this.permit()?.draft.sponsorId === this.actorId())),
  );
  protected readonly canManageAttachments = computed(
    () => this.canManageDraftAttachments() || this.canUploadFieldCopy(),
  );
  protected readonly fieldCopyOnly = computed(() =>
    ['ISSUED', 'SUSPENDED', 'EXPIRED', 'CLOSURE_REQUESTED', 'CLOSED'].includes(
      this.permit()?.status ?? '',
    ),
  );
  protected readonly fieldCopyUploadPackages = computed(() => {
    const closurePackageId = this.permit()?.workflow.closure.printPackageId;
    if (this.closureReplacementPending() && closurePackageId) {
      return this.readyPrintPackages().filter((item) => item.id === closurePackageId);
    }
    return this.readyPrintPackages();
  });
  protected readonly eligibleSignedFieldCopies = computed(() => {
    const items = this.signedFieldCopies();
    return items.filter(
      (item) =>
        item.category === 'SIGNED_FIELD_COPY' &&
        item.scanStatus === 'CLEAN' &&
        !items.some((candidate) => candidate.supersedesAttachmentId === item.id),
    );
  });
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
      (this.roles().includes('Sponsor') || this.roles().includes('Administrator')) &&
      !this.editing(),
  );
  protected readonly currentTask = computed(() =>
    this.tasks().find((task) => task.permitId === this.permit()?.id),
  );
  protected readonly canValidateHse = computed(
    () =>
      this.currentTask()?.type === 'HSE_VALIDATION' &&
      this.roles().includes('HSEValidator') &&
      this.permit()?.draft.sponsorId !== this.actorId(),
  );
  protected readonly canApprove = computed(
    () =>
      this.currentTask()?.type === 'AREA_APPROVE_AND_ISSUE' &&
      this.roles().includes('AreaOwnerManager') &&
      this.permit()?.workflow.areaOperations.completed,
  );
  protected readonly canReviewAreaOperations = computed(
    () =>
      this.currentTask()?.type === 'AREA_OPERATION_REVIEW' &&
      this.roles().includes('AreaOwnerSeniorOfficer'),
  );
  protected readonly approvalMissingSafetyEquipment = computed(
    () =>
      this.canApprove() && (this.permit()?.workflow.hse.safetyEquipmentCodes?.length ?? 0) === 0,
  );
  protected readonly canDisposition = computed(() => {
    const task = this.currentTask();
    return (
      !!task &&
      ((task.type === 'HSE_VALIDATION' && this.roles().includes('HSEValidator')) ||
        (task.type === 'AREA_OPERATION_REVIEW' &&
          this.roles().includes('AreaOwnerSeniorOfficer')) ||
        (task.type === 'AREA_APPROVE_AND_ISSUE' && this.roles().includes('AreaOwnerManager')))
    );
  });
  protected readonly hasCommandLocationScope = computed(() => {
    const locationId = this.permit()?.draft.locationId;
    const scopes = this.identity()?.locationScopes ?? [];
    return !!locationId && (scopes.includes('*') || scopes.includes(locationId));
  });
  protected readonly canSuspend = computed(
    () =>
      this.hasCommandLocationScope() &&
      this.permit()?.status === 'ISSUED' &&
      this.roles().some((role) =>
        ['HSEValidator', 'AreaOwnerManager', 'Administrator'].includes(role),
      ),
  );
  protected readonly canRequestRenewal = computed(
    () =>
      ['ISSUED', 'EXPIRED'].includes(this.permit()?.status ?? '') &&
      !this.permit()?.renewalPermitId &&
      !this.permit()?.workflow.closure.requested &&
      this.permit()?.workflow.renewal?.status !== 'PENDING' &&
      this.roles().includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId(),
  );
  protected readonly canRequestClosure = computed(
    () =>
      ['ISSUED', 'SUSPENDED', 'EXPIRED'].includes(this.permit()?.status ?? '') &&
      !this.permit()?.workflow.closure.requested &&
      !this.permit()?.renewalPermitId &&
      !['PENDING', 'REVISION_REQUIRED'].includes(this.permit()?.workflow.renewal?.status ?? '') &&
      this.roles().includes('Sponsor') &&
      this.permit()?.draft.sponsorId === this.actorId(),
  );
  protected readonly canReviewRenewal = computed(
    () =>
      this.currentTask()?.type === 'AREA_RENEWAL_REVIEW' &&
      this.roles().includes('AreaOwnerManager'),
  );
  protected readonly canReviewClosure = computed(
    () =>
      this.currentTask()?.type === 'AREA_CLOSE_VERIFICATION' &&
      this.roles().some((role) => ['AreaOwnerSeniorOfficer', 'AreaOwnerManager'].includes(role)),
  );
  protected readonly canClose = computed(
    () => this.canReviewClosure() && !this.closureReplacementPending(),
  );
  protected readonly canResolveSuspension = computed(
    () =>
      this.hasCommandLocationScope() &&
      this.permit()?.status === 'SUSPENDED' &&
      this.roles().some((role) => ['AreaOwnerManager', 'Administrator'].includes(role)),
  );
  protected readonly hasDecisionAction = computed(
    () =>
      this.canValidateHse() ||
      this.canReviewAreaOperations() ||
      this.canApprove() ||
      this.canDisposition() ||
      this.canReviewRenewal() ||
      this.canClose() ||
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
    headerClassificationCodes: this.fb.nonNullable.control<string[]>([], {
      validators: [Validators.required],
    }),
    workTypeCodes: this.fb.nonNullable.control<string[]>([], {
      validators: [Validators.required],
    }),
    otherWorkTypeDescription: ['', Validators.maxLength(80)],
    requiredDocumentCodes: this.fb.nonNullable.control<string[]>(['JSA', 'WORK_PROCEDURE']),
    equipmentTag: [''],
    equipmentName: ['', Validators.maxLength(100)],
    workOrderNumber: ['', Validators.maxLength(60)],
    additionalHazardReference: ['', Validators.maxLength(160)],
    plantArea: ['', Validators.required],
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
    Validators.pattern(/\S/),
    Validators.maxLength(1000),
  ]);
  protected readonly hseSafetyEquipmentCodes = this.fb.nonNullable.control<string[]>([], {
    validators: [Validators.required],
  });
  protected readonly areaOperationForm = this.fb.nonNullable.group({
    conditionCodes: this.fb.nonNullable.control<string[]>([]),
    otherConditionDetail: ['', Validators.maxLength(200)],
    conditionsReviewed: [false, Validators.requiredTrue],
  });
  protected readonly renewalForm = this.fb.nonNullable.group({
    validFrom: ['', Validators.required],
    validUntil: ['', Validators.required],
    printPackageId: ['', Validators.required],
    signedFieldCopyAttachmentId: ['', Validators.required],
    continuationStatement: ['', [Validators.required, Validators.maxLength(1000)]],
    allPagesReviewed: [false, Validators.requiredTrue],
    readableAndCompleteAcknowledged: [false, Validators.requiredTrue],
  });
  protected readonly closureForm = this.fb.nonNullable.group({
    printPackageId: ['', Validators.required],
    signedFieldCopyAttachmentId: ['', Validators.required],
    completionStatement: ['', [Validators.required, Validators.maxLength(1000)]],
    allPagesReviewed: [false, Validators.requiredTrue],
    readableAndCompleteAcknowledged: [false, Validators.requiredTrue],
  });
  protected readonly renewalDecisionForm = this.fb.nonNullable.group({
    fieldVerificationConfirmed: [false, Validators.requiredTrue],
    evidenceReadable: [false, Validators.requiredTrue],
  });
  protected readonly closureDecisionForm = this.fb.nonNullable.group({
    completionOutcome: ['COMPLETED', Validators.required],
    workAreaInspectedAndClean: [false],
    incompleteWorkStatus: ['', Validators.maxLength(800)],
    officerName: ['', [Validators.required, Validators.maxLength(100)]],
    managerAgreesWorkCompleted: [false],
    inhibitedSystemsRestored: [false],
    areaHandedBackAndSafeguardsRestored: [false],
    evidenceReadable: [false],
  });

  constructor() {
    this.identityApi
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (identity) => {
          this.identity.set(identity);
          this.identityError.set('');
        },
        error: (response) =>
          this.identityError.set(
            response?.error?.detail ??
              'Identitas pengguna gagal dimuat. Muat ulang halaman sebelum menjalankan tindakan workflow.',
          ),
      });
    this.loadLocations();
    this.loadHeaderClassifications();
    this.loadWorkTypes();
    this.loadSafetyEquipment();
    this.loadOperationalConditions();
    this.loadMandatoryDocuments();
    this.loadSupportingDocuments();
    this.form.controls.permitClass.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.reconcileHeaderClassificationSelection();
        this.reconcileWorkTypeSelection();
      });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.permitId = params.get('id') ?? '';
      this.editing.set(false);
      this.showingRenewalForm.set(false);
      this.showingClosureForm.set(false);
      this.renewalError.set('');
      this.renewalConflict.set(false);
      this.closureError.set('');
      this.closureConflict.set(false);
      this.renewalCreatedId.set(null);
      this.permit.set(null);
      this.hseSafetyEquipmentCodes.reset([]);

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
      headerClassificationCodes: permit.draft.headerClassificationCodes ?? [],
      workTypeCodes:
        permit.draft.workTypeCodes ??
        (permit.draft.workTypeCode ? [permit.draft.workTypeCode] : []),
      otherWorkTypeDescription: permit.draft.otherWorkTypeDescription ?? '',
      requiredDocumentCodes: [
        ...new Set(['JSA', 'WORK_PROCEDURE', ...permit.draft.requiredDocumentCodes]),
      ],
      equipmentTag: permit.draft.equipmentTag ?? '',
      equipmentName: permit.draft.equipmentName ?? '',
      workOrderNumber: permit.draft.workOrderNumber ?? '',
      additionalHazardReference: permit.draft.additionalHazardReference ?? '',
      plantArea: permit.draft.plantArea ?? '',
      jsaDocumentNumber: permit.draft.jsaDocumentNumber ?? '',
      jsaRevision: permit.draft.jsaRevision ?? '',
      jsaDate: permit.draft.jsaDate?.slice(0, 10) ?? '',
      validFrom: toLocalInput(permit.draft.validFrom),
      validUntil: toLocalInput(permit.draft.validUntil),
      eSimiNumber: permit.draft.eSimiNumber ?? '',
    });
    this.reconcileHeaderClassificationSelection();
    this.syncOtherWorkTypeValidation();
    this.reconcileSupportingDocumentSelection();
    this.error.set('');
    this.success.set('');
    this.closureError.set('');
    this.closureConflict.set(false);
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
      workTypeCode: null,
      otherWorkTypeDescription: value.otherWorkTypeDescription.trim() || null,
      equipmentTag: value.equipmentTag.trim() || null,
      equipmentName: value.equipmentName.trim() || null,
      workOrderNumber: value.workOrderNumber.trim() || null,
      additionalHazardReference: value.additionalHazardReference.trim() || null,
      plantArea: value.plantArea.trim() || null,
      clsrApplicable: false,
      // SIMOPS is not authored in the current controlled template. Preserve legacy data until
      // the API contract is retired through an explicit compatibility change.
      simopsDeclaration: permit.draft.simopsDeclaration ?? null,
      isolationPrecautionCodes: [],
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

  protected headerClassificationOptions(): PermitHeaderClassificationOption[] {
    return this.headerClassificationCatalog()[this.form.controls.permitClass.value]?.options ?? [];
  }

  protected headerClassificationSelectionMode(): 'NONE' | 'SINGLE' | 'MULTIPLE' {
    return (
      this.headerClassificationCatalog()[this.form.controls.permitClass.value]?.selectionMode ??
      'NONE'
    );
  }

  protected isHeaderClassificationSelected(code: string): boolean {
    return this.form.controls.headerClassificationCodes.value.includes(code);
  }

  protected toggleHeaderClassification(code: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.form.controls.headerClassificationCodes.value;
    const next =
      this.headerClassificationSelectionMode() === 'SINGLE'
        ? checked
          ? [code]
          : []
        : checked
          ? [...new Set([...current, code])]
          : current.filter((item) => item !== code);
    this.form.controls.headerClassificationCodes.setValue(next);
    this.form.controls.headerClassificationCodes.markAsTouched();
    this.syncLegacyRiskLevel(next);
  }

  protected headerClassificationLabels(draft: PermitDraft): string {
    const selected = draft.headerClassificationCodes ?? [];
    if (draft.permitClass === 'ConfinedSpaceEntry') {
      return 'CSE - Confined Space Entry';
    }

    const options = this.headerClassificationCatalog()[draft.permitClass]?.options ?? [];
    return (
      selected
        .map((code) => options.find((option) => option.code === code)?.label ?? code)
        .join(', ') || 'Belum diisi'
    );
  }

  protected permitClassLabel(value: string): string {
    const labels: Record<string, string> = {
      HotWork: 'Pekerjaan Panas',
      ColdWork: 'Pekerjaan Dingin',
      ConfinedSpaceEntry: 'Memasuki Ruang Terbatas',
    };
    return labels[value] ?? 'Kelas izin tidak dikenal';
  }

  protected submitterTypeLabel(value: PermitDraft['submitterType']): string {
    const labels: Record<NonNullable<PermitDraft['submitterType']>, string> = {
      CONTRACTOR: 'Kontraktor',
      USER_SPONSOR: 'User Sponsor',
    };
    return labels[value ?? 'USER_SPONSOR'];
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
    this.syncOtherWorkTypeValidation();
  }

  protected hasOtherWorkTypeSelected(): boolean {
    const selected = new Set(this.form.controls.workTypeCodes.value);
    return this.workTypeOptions().some(
      (option) => option.requiresDetail && selected.has(option.code),
    );
  }

  protected isSupportingDocumentSelected(code: string): boolean {
    return this.form.controls.requiredDocumentCodes.value.includes(code);
  }

  protected additionalSupportingDocumentOptions(): PermitSupportingDocumentOption[] {
    return this.supportingDocumentOptions().filter(
      (option) => !option.required && option.code !== 'WORK_PROCEDURE',
    );
  }

  protected toggleSupportingDocument(option: PermitSupportingDocumentOption, event: Event): void {
    if (option.required) return;
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.form.controls.requiredDocumentCodes.value;
    this.form.controls.requiredDocumentCodes.setValue(
      checked
        ? [...new Set([...current, option.code])]
        : current.filter((code) => code !== option.code),
    );
  }

  protected workTypeLabels(draft: PermitDraft): string {
    const codes = draft.workTypeCodes ?? (draft.workTypeCode ? [draft.workTypeCode] : []);
    const options = this.workTypeCatalog()[draft.permitClass] ?? [];
    return (
      codes
        .map((code) => {
          const option = options.find((item) => item.code === code);
          if (option?.requiresDetail && draft.otherWorkTypeDescription) {
            return `${option.label.replace(/\s*:\s*$/, '')}: ${draft.otherWorkTypeDescription}`;
          }
          return option?.label ?? code;
        })
        .join(', ') || 'Belum diisi'
    );
  }

  protected safetyEquipmentOptions(): PermitSafetyEquipmentOption[] {
    const permitClass = this.permit()?.draft.permitClass ?? '';
    return this.safetyEquipmentCatalog()[permitClass] ?? [];
  }

  protected selectedSafetyEquipmentLabels(): string[] {
    const selectedCodes = this.permit()?.workflow.hse.safetyEquipmentCodes ?? [];
    const options = this.safetyEquipmentOptions();
    return selectedCodes.map(
      (code) =>
        options.find((option) => option.code === code)?.label ?? 'Item katalog tidak tersedia',
    );
  }

  protected isSafetyEquipmentSelected(code: string): boolean {
    return this.hseSafetyEquipmentCodes.value.includes(code);
  }

  protected toggleSafetyEquipment(code: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const current = this.hseSafetyEquipmentCodes.value;
    const next = checked
      ? [...new Set([...current, code])]
      : current.filter((item) => item !== code);
    this.hseSafetyEquipmentCodes.setValue(next);
    this.hseSafetyEquipmentCodes.markAsTouched();
  }

  protected operationalConditionParents(): PermitOperationalConditionOption[] {
    return this.operationalConditionOptions().filter((option) => !option.parentCode);
  }

  protected operationalConditionChildren(parentCode: string): PermitOperationalConditionOption[] {
    return this.operationalConditionOptions().filter((option) => option.parentCode === parentCode);
  }

  protected isOperationalConditionSelected(code: string): boolean {
    return this.areaOperationForm.controls.conditionCodes.value.includes(code);
  }

  protected toggleOperationalCondition(
    option: PermitOperationalConditionOption,
    event: Event,
  ): void {
    const checked = (event.target as HTMLInputElement).checked;
    const control = this.areaOperationForm.controls.conditionCodes;
    let next = new Set(control.value);
    if (checked) {
      next.add(option.code);
      if (option.parentCode) next.add(option.parentCode);
    } else {
      next.delete(option.code);
      if (!option.parentCode) {
        for (const child of this.operationalConditionChildren(option.code)) {
          next.delete(child.code);
        }
      }
      if (option.code === 'OPS_OTHER') {
        this.areaOperationForm.controls.otherConditionDetail.setValue('');
      }
    }
    control.setValue([...next]);
    control.markAsTouched();
    this.syncOperationalConditionDetailValidation();
  }

  protected operationalSelectionValid(): boolean {
    const selected = new Set(this.areaOperationForm.controls.conditionCodes.value);
    const hasChild = (parent: string) =>
      this.operationalConditionChildren(parent).some((child) => selected.has(child.code));
    if (selected.has('OPS_ISOLATION') && !hasChild('OPS_ISOLATION')) return false;
    if (selected.has('OPS_FLUSHING') && !hasChild('OPS_FLUSHING')) return false;
    if (
      selected.has('OPS_OTHER') &&
      !this.areaOperationForm.controls.otherConditionDetail.value.trim()
    ) {
      return false;
    }
    return true;
  }

  protected operationalConditionLabel(code: string): string {
    return this.operationalConditionOptions().find((option) => option.code === code)?.label ?? code;
  }

  private syncOperationalConditionDetailValidation(): void {
    const detail = this.areaOperationForm.controls.otherConditionDetail;
    const selected = this.areaOperationForm.controls.conditionCodes.value.includes('OPS_OTHER');
    detail.setValidators(
      selected ? [Validators.required, Validators.maxLength(200)] : [Validators.maxLength(200)],
    );
    detail.updateValueAndValidity({ emitEvent: false });
  }

  protected applyAttachmentPermitChange(change: PermitAttachmentPermitChange): void {
    this.permit.update((permit) =>
      permit ? { ...permit, eTag: change.eTag, version: change.version } : permit,
    );
    this.loadSignedFieldCopies();
  }

  protected fieldCopiesForPackage(printPackageId: string): PermitAttachment[] {
    return this.eligibleSignedFieldCopies().filter(
      (attachment) => attachment.printPackageId === printPackageId,
    );
  }

  protected closureFieldCopiesForPackage(printPackageId: string): PermitAttachment[] {
    const currentEvidence = new Set(
      this.canResubmitClosure()
        ? (this.permit()?.workflow.closure.signedFieldCopyAttachmentIds ?? [])
        : [],
    );
    return this.fieldCopiesForPackage(printPackageId).filter(
      (attachment) => !currentEvidence.has(attachment.id),
    );
  }

  protected printPackageLabel(printPackageId: string): string {
    const item = this.readyPrintPackages().find((option) => option.id === printPackageId);
    return item ? `Versi ${item.permitVersion}` : 'Paket cetak penutupan saat ini';
  }

  protected openRenewalForm(): void {
    const permit = this.permit();
    if (!permit || !this.canRequestRenewal()) return;
    this.renewalForm.reset({
      validFrom: toLocalInput(permit.draft.validUntil),
      validUntil: '',
      printPackageId: '',
      signedFieldCopyAttachmentId: '',
      continuationStatement: '',
      allPagesReviewed: false,
      readableAndCompleteAcknowledged: false,
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
        printPackageId: value.printPackageId,
        signedFieldCopyAttachmentIds: [value.signedFieldCopyAttachmentId],
        continuationStatement: value.continuationStatement,
        allPagesReviewed: value.allPagesReviewed,
        readableAndCompleteAcknowledged: value.readableAndCompleteAcknowledged,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.permit.set(updated);
          this.refreshTasks();
          this.showingRenewalForm.set(false);
          this.saving.set(false);
          this.renewalError.set('');
          this.renewalConflict.set(false);
          this.success.set('Permintaan perpanjangan dikirim ke Pemilik Wilayah untuk ditinjau.');
        },
        error: (response) => {
          this.saving.set(false);
          this.renewalConflict.set(response.status === 409);
          this.renewalError.set(response?.error?.detail ?? 'Pengajuan renewal gagal diproses.');
        },
      });
  }

  protected openClosureForm(): void {
    const permit = this.permit();
    if (!permit || (!this.canRequestClosure() && !this.canResubmitClosure())) return;
    const resubmitting = this.canResubmitClosure();
    this.closureForm.reset({
      printPackageId: resubmitting ? (permit.workflow.closure.printPackageId ?? '') : '',
      signedFieldCopyAttachmentId: '',
      completionStatement: resubmitting ? (permit.workflow.closure.completionStatement ?? '') : '',
      allPagesReviewed: false,
      readableAndCompleteAcknowledged: false,
    });
    this.error.set('');
    this.success.set('');
    this.closureError.set('');
    this.closureConflict.set(false);
    this.showingClosureForm.set(true);
  }

  protected cancelClosure(): void {
    this.showingClosureForm.set(false);
    this.closureError.set('');
    this.closureConflict.set(false);
    this.closureForm.reset();
  }

  protected requestClosure(): void {
    const permit = this.permit();
    if (!permit || this.closureForm.invalid) {
      this.closureForm.markAllAsTouched();
      return;
    }
    const value = this.closureForm.getRawValue();
    const resubmitting = this.canResubmitClosure();
    this.saving.set(true);
    this.success.set('');
    this.closureError.set('');
    this.closureConflict.set(false);
    const request = {
      printPackageId: value.printPackageId,
      signedFieldCopyAttachmentIds: [value.signedFieldCopyAttachmentId],
      completionStatement: value.completionStatement,
      allPagesReviewed: value.allPagesReviewed,
      readableAndCompleteAcknowledged: value.readableAndCompleteAcknowledged,
    };
    const command = resubmitting
      ? this.api.resubmitClosure(permit.id, permit.eTag, request)
      : this.api.requestClosure(permit.id, permit.eTag, request);
    command.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (updated) => {
        this.permit.set(updated);
        this.refreshTasks();
        this.showingClosureForm.set(false);
        this.saving.set(false);
        this.success.set(
          resubmitting
            ? 'Hardcopy terbaru diajukan ulang. Pemilik Wilayah dapat melanjutkan verifikasi penutupan.'
            : 'Penyelesaian pekerjaan diajukan. Pemilik Wilayah akan memverifikasi hardcopy dan handback.',
        );
      },
      error: (response) => {
        this.saving.set(false);
        this.closureConflict.set(response.status === 409);
        this.closureError.set(response?.error?.detail ?? 'Pengajuan penutupan gagal diproses.');
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
    const task = this.currentTask();
    const permit = this.permit();
    if (
      !task ||
      !permit ||
      this.decisionStatement.invalid ||
      this.hseSafetyEquipmentCodes.invalid
    ) {
      this.decisionStatement.markAsTouched();
      this.hseSafetyEquipmentCodes.markAsTouched();
      return;
    }

    this.runCommand(
      this.api.validate(task.id, permit.eTag, {
        statement: this.decisionStatement.getRawValue(),
        safetyEquipmentCodes: this.hseSafetyEquipmentCodes.getRawValue(),
      }),
      'Validasi PIC HSE tersimpan. APD/perlengkapan safety akan dicentang pada Bagian 5 paket cetak.',
    );
  }

  protected reviewAreaOperations(): void {
    const task = this.currentTask();
    const permit = this.permit();
    this.syncOperationalConditionDetailValidation();
    if (
      !task ||
      !permit ||
      this.decisionStatement.invalid ||
      this.areaOperationForm.invalid ||
      !this.operationalSelectionValid()
    ) {
      this.decisionStatement.markAsTouched();
      this.areaOperationForm.markAllAsTouched();
      return;
    }

    const review = this.areaOperationForm.getRawValue();
    this.runCommand(
      this.api.reviewAreaOperations(task.id, permit.eTag, {
        statement: this.decisionStatement.getRawValue(),
        conditionCodes: review.conditionCodes,
        otherConditionDetail: review.otherConditionDetail.trim() || null,
        conditionsReviewed: review.conditionsReviewed,
      }),
      'Verifikasi SO/Officer tersimpan. PTW diteruskan kepada Manager Pemilik Wilayah untuk approval final.',
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

  protected approveRenewal(): void {
    const task = this.currentTask();
    const permit = this.permit();
    if (!task || !permit || this.decisionStatement.invalid || this.renewalDecisionForm.invalid) {
      this.decisionStatement.markAsTouched();
      this.renewalDecisionForm.markAllAsTouched();
      return;
    }

    const confirmations = this.renewalDecisionForm.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.api
      .approveRenewal(task.id, permit.eTag, {
        statement: this.decisionStatement.getRawValue(),
        fieldVerificationConfirmed: confirmations.fieldVerificationConfirmed,
        evidenceReadable: confirmations.evidenceReadable,
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
                  workflow: {
                    ...current.workflow,
                    renewal: current.workflow.renewal
                      ? { ...current.workflow.renewal, status: 'APPROVED' }
                      : current.workflow.renewal,
                  },
                }
              : current,
          );
          this.renewalCreatedId.set(result.renewal.id);
          this.refreshTasks();
          this.saving.set(false);
          this.decisionStatement.reset();
          this.renewalDecisionForm.reset();
          this.success.set(
            'Perpanjangan disetujui dan draft PTW penerus dibuat. Draft baru tetap mengikuti validasi HSE dan penerbitan normal.',
          );
        },
        error: (response) => {
          this.saving.set(false);
          this.conflict.set(response.status === 409);
          this.error.set(response?.error?.detail ?? 'Approval perpanjangan gagal diproses.');
        },
      });
  }

  protected requestRenewalEvidence(): void {
    this.runTaskDecision(
      (task, permit, reason) => this.api.requestRenewalEvidence(task.id, permit.eTag, reason),
      'Permintaan evidence baru dikirim kepada Sponsor. Review lama telah ditutup.',
    );
  }

  protected rejectRenewal(): void {
    if (!globalThis.confirm('Tolak permintaan perpanjangan ini?')) return;
    this.runTaskDecision(
      (task, permit, reason) => this.api.rejectRenewal(task.id, permit.eTag, reason),
      'Permintaan perpanjangan ditolak. PTW asal tidak diubah.',
    );
  }

  protected requestClosureEvidence(): void {
    const task = this.currentTask();
    const permit = this.permit();
    const verification = this.closureDecisionForm.getRawValue();
    if (verification.completionOutcome === 'INCOMPLETE') {
      const status = verification.incompleteWorkStatus.trim();
      if (
        !task ||
        !permit ||
        this.decisionStatement.invalid ||
        this.closureDecisionForm.controls.officerName.invalid ||
        !status
      ) {
        this.decisionStatement.markAsTouched();
        this.closureDecisionForm.controls.officerName.markAsTouched();
        this.closureDecisionForm.controls.incompleteWorkStatus.markAsTouched();
        return;
      }

      const reason = `${this.decisionStatement.getRawValue().trim()} Pekerjaan belum selesai - Officer ${verification.officerName.trim()}: ${status}`;
      this.runCommand(
        this.api.requestClosureEvidence(task.id, permit.eTag, reason),
        'Tindak lanjut pekerjaan dikirim kepada Sponsor. PTW belum ditutup dan hak kerja tidak dipulihkan.',
      );
      return;
    }

    this.runTaskDecision(
      (task, permit, reason) => this.api.requestClosureEvidence(task.id, permit.eTag, reason),
      'Sponsor diminta mengganti evidence hardcopy sebelum penutupan.',
    );
  }

  protected closePermit(): void {
    const confirmations = this.closureDecisionForm.getRawValue();
    if (!this.closureReadyToClose() || this.decisionStatement.invalid) {
      this.closureDecisionForm.markAllAsTouched();
      this.decisionStatement.markAsTouched();
      return;
    }
    this.runTaskDecision(
      (task, permit, statement) =>
        this.api.close(task.id, permit.eTag, {
          statement,
          officerName: confirmations.officerName.trim(),
          workAreaInspectedAndClean: confirmations.workAreaInspectedAndClean,
          workCompleted: confirmations.completionOutcome === 'COMPLETED',
          managerAgreesWorkCompleted: confirmations.managerAgreesWorkCompleted,
          inhibitedSystemsRestored: confirmations.inhibitedSystemsRestored,
          areaHandedBackAndSafeguardsRestored: confirmations.areaHandedBackAndSafeguardsRestored,
          evidenceReadable: confirmations.evidenceReadable,
        }),
      'PTW ditutup setelah seluruh checklist Bagian 10 dan hardcopy terverifikasi.',
    );
  }

  protected closureReadyToClose(): boolean {
    const verification = this.closureDecisionForm.getRawValue();
    return (
      verification.completionOutcome === 'COMPLETED' &&
      verification.workAreaInspectedAndClean &&
      !!verification.officerName.trim() &&
      verification.managerAgreesWorkCompleted &&
      verification.inhibitedSystemsRestored &&
      verification.areaHandedBackAndSafeguardsRestored &&
      verification.evidenceReadable
    );
  }

  protected closureFollowUpReady(): boolean {
    const verification = this.closureDecisionForm.getRawValue();
    if (verification.completionOutcome === 'INCOMPLETE') {
      return (
        this.decisionStatement.valid &&
        !!verification.officerName.trim() &&
        !!verification.incompleteWorkStatus.trim()
      );
    }

    return this.decisionStatement.valid;
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
        this.areaOperationForm.reset({
          conditionCodes: updated.workflow.areaOperations.conditionCodes ?? [],
          otherConditionDetail: updated.workflow.areaOperations.otherConditionDetail ?? '',
          conditionsReviewed: updated.workflow.areaOperations.completed,
        });
        this.syncOperationalConditionDetailValidation();
        this.renewalDecisionForm.reset();
        this.closureDecisionForm.reset();
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
          this.hseSafetyEquipmentCodes.setValue(permit.workflow.hse.safetyEquipmentCodes ?? []);
          this.areaOperationForm.reset({
            conditionCodes: permit.workflow.areaOperations.conditionCodes ?? [],
            otherConditionDetail: permit.workflow.areaOperations.otherConditionDetail ?? '',
            conditionsReviewed: permit.workflow.areaOperations.completed,
          });
          this.syncOperationalConditionDetailValidation();
          this.refreshTasks();
          this.loadSignedFieldCopies();
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
    this.taskError.set('');
    this.api
      .listTasks()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => this.tasks.set(page.items),
        error: (response) => {
          this.tasks.set([]);
          this.taskError.set(
            response?.error?.detail ??
              'Tugas workflow gagal dimuat. Muat ulang halaman sebelum memberikan keputusan.',
          );
        },
      });
  }

  private loadSignedFieldCopies(): void {
    if (!this.permitId) return;
    this.attachmentApi
      .list(this.permitId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) =>
          this.signedFieldCopies.set(
            items.filter((attachment) => attachment.category === 'SIGNED_FIELD_COPY'),
          ),
        error: () => this.signedFieldCopies.set([]),
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

  private loadHeaderClassifications(): void {
    this.api
      .listHeaderClassifications()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.headerClassificationCatalog.set(
            Object.fromEntries(catalog.map((item) => [item.permitClass, item])),
          );
          this.loadingHeaderClassifications.set(false);
          if (this.editing()) {
            this.reconcileHeaderClassificationSelection();
          }
        },
        error: (response) => {
          this.loadingHeaderClassifications.set(false);
          this.headerClassificationError.set(
            response?.error?.detail ??
              'Klasifikasi header izin gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });
  }

  private loadSafetyEquipment(): void {
    this.api
      .listSafetyEquipment()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (catalog) => {
          this.safetyEquipmentCatalog.set(
            Object.fromEntries(catalog.map((item) => [item.permitClass, item.options])),
          );
          this.loadingSafetyEquipment.set(false);
        },
        error: (response) => {
          this.loadingSafetyEquipment.set(false);
          this.safetyEquipmentError.set(
            response?.error?.detail ??
              'Daftar APD/perlengkapan safety gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });
  }

  private loadOperationalConditions(): void {
    this.api
      .listOperationalConditions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.operationalConditionOptions.set(options);
          this.loadingOperationalConditions.set(false);
        },
        error: (response) => {
          this.loadingOperationalConditions.set(false);
          this.operationalConditionError.set(
            response?.error?.detail ??
              'Daftar kondisi operasi Bagian 7 gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });
  }

  private loadSupportingDocuments(): void {
    this.api
      .listSupportingDocuments()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.supportingDocumentOptions.set(options);
          this.loadingSupportingDocuments.set(false);
          this.reconcileSupportingDocumentSelection();
        },
        error: (response) => {
          this.loadingSupportingDocuments.set(false);
          this.supportingDocumentError.set(
            response?.error?.detail ??
              'Daftar dokumen pendukung gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });
  }

  private loadMandatoryDocuments(): void {
    this.api
      .listMandatoryDocuments()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.mandatoryDocumentOptions.set(options);
          this.loadingMandatoryDocuments.set(false);
        },
        error: (response) => {
          this.loadingMandatoryDocuments.set(false);
          this.mandatoryDocumentError.set(
            response?.error?.detail ??
              'Daftar dokumen wajib gagal dimuat. Coba muat ulang halaman.',
          );
        },
      });
  }

  private reconcileWorkTypeSelection(): void {
    const validCodes = new Set(this.workTypeOptions().map((option) => option.code));
    const current = this.form.controls.workTypeCodes.value;
    const next = current.filter((code) => validCodes.has(code));
    if (next.length !== current.length) {
      this.form.controls.workTypeCodes.setValue(next);
    }
    this.syncOtherWorkTypeValidation();
  }

  private reconcileHeaderClassificationSelection(): void {
    const control = this.form.controls.headerClassificationCodes;
    const configuration = this.headerClassificationCatalog()[this.form.controls.permitClass.value];
    if (!configuration) return;
    const validCodes = new Set(configuration.options.map((option) => option.code));
    let next = control.value.filter((code) => validCodes.has(code));
    if (configuration.selectionMode === 'SINGLE' && next.length > 1) {
      next = next.slice(0, 1);
    }

    control.setValue(next);
    if (configuration.selectionMode === 'NONE') {
      control.clearValidators();
    } else {
      control.setValidators([Validators.required]);
    }
    control.updateValueAndValidity({ emitEvent: false });
    this.syncLegacyRiskLevel(next);
  }

  private syncLegacyRiskLevel(classificationCodes: string[]): void {
    if (this.form.controls.permitClass.value === 'ColdWork') {
      if (classificationCodes.includes('COLD_LOW_RISK')) {
        this.form.controls.riskLevel.setValue('Low');
      } else if (classificationCodes.includes('COLD_HIGH_RISK')) {
        this.form.controls.riskLevel.setValue('High');
      }
    }
  }

  private syncOtherWorkTypeValidation(): void {
    const control = this.form.controls.otherWorkTypeDescription;
    if (this.hasOtherWorkTypeSelected()) {
      control.setValidators([Validators.required, Validators.maxLength(80)]);
    } else {
      control.clearValidators();
      control.setValidators([Validators.maxLength(80)]);
      control.setValue('');
    }
    control.updateValueAndValidity({ emitEvent: false });
  }

  private reconcileSupportingDocumentSelection(): void {
    const validCodes = new Set(this.supportingDocumentOptions().map((option) => option.code));
    const requiredCodes = this.supportingDocumentOptions()
      .filter((option) => option.required)
      .map((option) => option.code);
    const current = this.form.controls.requiredDocumentCodes.value.filter((code) =>
      validCodes.has(code),
    );
    this.form.controls.requiredDocumentCodes.setValue([...new Set([...requiredCodes, ...current])]);
  }
}
