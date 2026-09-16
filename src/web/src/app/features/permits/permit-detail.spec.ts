import { Component, input, output } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup } from '@angular/forms';
import { ActivatedRoute, convertToParamMap, ParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { LocationApi } from '../../core/location-api';
import {
  Permit,
  PermitApi,
  PermitRenewalResult,
  PermitTask,
  RequestPermitRenewal,
  ValidateSubmissionRequest,
} from '../../core/permit-api';
import { PermitAttachmentPermitChange, PermitAttachments } from './permit-attachments';
import { PermitDetail } from './permit-detail';
import { PermitHistory } from './permit-history';
import { PermitPrintPackages } from './permit-print-packages';
import { PermitValidationProgress } from './permit-validation-progress';

@Component({ selector: 'app-permit-attachments', template: '' })
class PermitAttachmentsStub {
  readonly permitId = input.required<string>();
  readonly eTag = input.required<string>();
  readonly canManage = input(false);
  readonly selectedDocumentCodes = input<string[]>([]);
  readonly jsaDocumentNumber = input('');
  readonly jsaRevision = input('');
  readonly jsaDate = input('');
  readonly printPackages = input<{ id: string; permitVersion: number }[]>([]);
  readonly permitChanged = output<PermitAttachmentPermitChange>();
}

@Component({ selector: 'app-permit-print-packages', template: '' })
class PermitPrintPackagesStub {
  readonly permitId = input.required<string>();
  readonly canRetry = input(false);
  readonly canPreview = input(false);
  readonly readyPackages = output<{ id: string; permitVersion: number }[]>();
}

@Component({ selector: 'app-permit-history', template: '' })
class PermitHistoryStub {
  readonly permitId = input.required<string>();
  readonly revision = input.required<number>();
}

@Component({ selector: 'app-permit-validation-progress', template: '' })
class PermitValidationProgressStub {
  readonly workflow = input.required<Permit['workflow']>();
}

const validation = (code: string, label: string) => ({
  code,
  label,
  completed: false,
  actorId: null,
  statement: null,
  completedAt: null,
  safetyEquipmentCodes: [],
});

describe('PermitDetail HSE validation', () => {
  it('submits the controlled Bagian 5 selection with the HSE decision', async () => {
    sessionStorage.setItem('ptw.development-identity', 'hse-validator');
    const submittedPermit: Permit = {
      ...permit,
      status: 'UNDER_VALIDATION',
      version: 2,
      draft: { ...permit.draft, safetyEquipmentCodes: [] },
      workflow: {
        ...permit.workflow,
        hse: validation('HSE', 'PIC HSE'),
        approval: {
          completed: false,
          actorId: null,
          actorPosition: null,
          capacity: null,
          principalManagerUserId: null,
          principalPosition: null,
          authorizationId: null,
          actingAssignmentId: null,
          statement: null,
          approvedAt: null,
        },
      },
    };

    let validationRequest: ValidateSubmissionRequest | null = null;
    const permitApi = {
      get: () => of(submittedPermit),
      listTasks: () =>
        of({
          items: [
            {
              id: 'hse-task-id',
              permitId: submittedPermit.id,
              permitVersion: submittedPermit.version,
              type: 'HSE_VALIDATION',
              label: 'Validasi PIC HSE',
              requiredRole: 'HSEValidator',
              status: 'PENDING',
              permitNumber: submittedPermit.permitNumber ?? null,
              permitTitle: submittedPermit.draft.title,
              locationId: submittedPermit.draft.locationId,
              createdAt: submittedPermit.createdAt,
              completedAt: null,
            },
          ],
          count: 1,
        }),
      listWorkTypes: () =>
        of([
          {
            permitClass: 'ColdWork',
            options: [{ code: 'COLD_MECHANICAL', label: 'Mekanikal' }],
          },
        ]),
      listSafetyEquipment: () =>
        of([
          {
            permitClass: 'ColdWork',
            options: [
              { code: 'SAFETY_FIRE_EXTINGUISHER', label: 'APAR' },
              { code: 'SAFETY_LOTO', label: 'LOTO' },
            ],
          },
        ]),
      listSupportingDocuments: () =>
        of([
          {
            code: 'JSA',
            label: 'Job Safety Analisis (JSA)',
            templateColumn: 0,
            templateIndex: 0,
            required: true,
            requiresMetadata: true,
          },
        ]),
      validate: (_taskId: string, _eTag: string, request: ValidateSubmissionRequest) => {
        validationRequest = request;
        return of({
          ...submittedPermit,
          status: 'AWAITING_AREA_APPROVAL',
          draft: { ...submittedPermit.draft, safetyEquipmentCodes: request.safetyEquipmentCodes },
        });
      },
    };

    await TestBed.configureTestingModule({
      imports: [PermitDetail],
      providers: [
        provideRouter([]),
        { provide: PermitApi, useValue: permitApi },
        { provide: LocationApi, useValue: { list: () => of({ items: [], count: 0 }) } },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: submittedPermit.id })) },
        },
      ],
    })
      .overrideComponent(PermitDetail, {
        remove: {
          imports: [
            PermitAttachments,
            PermitHistory,
            PermitPrintPackages,
            PermitValidationProgress,
          ],
        },
        add: {
          imports: [
            PermitAttachmentsStub,
            PermitHistoryStub,
            PermitPrintPackagesStub,
            PermitValidationProgressStub,
          ],
        },
      })
      .compileComponents();

    const fixture = TestBed.createComponent(PermitDetail);
    fixture.detectChanges();

    const options = Array.from<HTMLInputElement>(
      fixture.nativeElement.querySelectorAll('.safety-equipment-option input'),
    );
    expect(options).toHaveLength(2);
    expect(
      options.every(
        (option) =>
          option.parentElement?.matches('label.safety-equipment-option') &&
          option.nextElementSibling?.matches('span'),
      ),
    ).toBe(true);
    options[0]!.checked = true;
    options[0]!.dispatchEvent(new Event('change'));

    const statement = (
      fixture.componentInstance as unknown as { decisionStatement: FormControl<string> }
    ).decisionStatement;
    statement.setValue('APD dan perlengkapan safety telah diverifikasi.');
    fixture.detectChanges();

    const validateButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((button) => button.textContent?.includes('Validasi sebagai PIC HSE'));
    validateButton?.click();

    expect(validationRequest).toEqual({
      statement: 'APD dan perlengkapan safety telah diverifikasi.',
      safetyEquipmentCodes: ['SAFETY_FIRE_EXTINGUISHER'],
    });
  });

  afterEach(() => {
    sessionStorage.removeItem('ptw.development-identity');
  });
});

const permit: Permit = {
  id: 'permit-id',
  permitNumber: 'PTW-TEST',
  status: 'ISSUED',
  version: 8,
  eTag: '"etag-value"',
  createdAt: '2026-09-04T01:00:00.000Z',
  updatedAt: '2026-09-04T02:00:00.000Z',
  draft: {
    title: 'Pekerjaan aktif',
    description: 'Uji error renewal',
    locationId: 'ORF',
    sponsorId: 'sponsor.demo',
    performingAuthority: 'Pelaksana',
    company: 'PT Mitra',
    permitClass: 'ColdWork',
    riskLevel: 'Low',
    validFrom: '2026-09-04T01:00:00.000Z',
    validUntil: '2026-09-04T12:00:00.000Z',
    hazards: ['Bahaya'],
    controls: ['Kontrol'],
    requiredDocumentCodes: [],
    workTypeCodes: ['COLD_MECHANICAL'],
  },
  workflow: {
    hse: validation('HSE', 'PIC HSE'),
    approval: {
      completed: true,
      actorId: 'area.owner.orf.demo',
      actorPosition: 'Manager',
      capacity: 'MANAGER',
      principalManagerUserId: 'area.owner.orf.demo',
      principalPosition: 'Manager',
      authorizationId: 'authorization-id',
      actingAssignmentId: null,
      statement: 'Disetujui',
      approvedAt: '2026-09-04T01:30:00.000Z',
    },
    suspension: {
      suspended: false,
      suspendedBy: null,
      reason: null,
      suspendedAt: null,
      resolvedBy: null,
      resolution: null,
      resolvedAt: null,
    },
    closure: {
      requested: false,
      printPackageId: null,
      signedFieldCopyAttachmentIds: [],
      requestedBy: null,
      completionStatement: null,
      requestedAt: null,
      revision: 0,
      replacementReason: null,
      closed: false,
      closedBy: null,
      closeStatement: null,
      closedAt: null,
    },
  },
};

describe('PermitDetail', () => {
  let routeParamMap: BehaviorSubject<ParamMap>;
  let requestedPermitIds: string[];
  let currentPermit: Permit;
  let currentTasks: PermitTask[];

  beforeEach(async () => {
    sessionStorage.setItem('ptw.development-identity', 'sponsor-admin');
    routeParamMap = new BehaviorSubject(convertToParamMap({ id: permit.id }));
    requestedPermitIds = [];
    currentPermit = permit;
    currentTasks = [];

    const permitApi = {
      get: (id: string) => {
        requestedPermitIds.push(id);
        return of({ ...currentPermit, id, permitNumber: `PTW-${id}` });
      },
      listTasks: () => of({ items: currentTasks, count: currentTasks.length }),
      listWorkTypes: () =>
        of([
          {
            permitClass: 'ColdWork',
            options: [{ code: 'COLD_MECHANICAL', label: 'Mekanikal' }],
          },
        ]),
      listSafetyEquipment: () => of([]),
      listSupportingDocuments: () =>
        of([
          {
            code: 'JSA',
            label: 'Job Safety Analisis (JSA)',
            templateColumn: 0,
            templateIndex: 0,
            required: true,
            requiresMetadata: true,
          },
        ]),
      requestRenewal: (
        _id: string,
        _eTag: string,
        _request: RequestPermitRenewal,
      ): Observable<PermitRenewalResult> =>
        throwError(() => ({
          status: 422,
          error: { detail: 'PTW asal sudah melewati masa berlaku.' },
        })),
    };

    await TestBed.configureTestingModule({
      imports: [PermitDetail],
      providers: [
        provideRouter([]),
        { provide: PermitApi, useValue: permitApi },
        { provide: LocationApi, useValue: { list: () => of({ items: [], count: 0 }) } },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: routeParamMap.asObservable() },
        },
      ],
    })
      .overrideComponent(PermitDetail, {
        remove: {
          imports: [
            PermitAttachments,
            PermitHistory,
            PermitPrintPackages,
            PermitValidationProgress,
          ],
        },
        add: {
          imports: [
            PermitAttachmentsStub,
            PermitHistoryStub,
            PermitPrintPackagesStub,
            PermitValidationProgressStub,
          ],
        },
      })
      .compileComponents();
  });

  afterEach(() => {
    sessionStorage.removeItem('ptw.development-identity');
  });

  it('keeps the page-level hardcopy safety warning visible', () => {
    const fixture = TestBed.createComponent(PermitDetail);
    fixture.detectChanges();

    const safetyNote = fixture.nativeElement.querySelector('.safety-note') as HTMLElement | null;
    expect(safetyNote?.textContent).toContain(
      'DITERBITKAN belum otomatis mengizinkan pekerjaan dimulai',
    );
    expect(safetyNote?.textContent).toContain('tanda tangan lapangan tetap wajib pada hardcopy');
  });

  it('blocks legacy area approval and directs the manager to request revision', () => {
    sessionStorage.setItem('ptw.development-identity', 'area-owner-orf');
    currentPermit = {
      ...permit,
      status: 'AWAITING_AREA_APPROVAL',
      workflow: {
        ...permit.workflow,
        hse: {
          code: 'HSE',
          label: 'Validasi PIC HSE',
          completed: true,
          actorId: 'hse.validator.demo',
          statement: 'Validasi lama.',
          completedAt: '2026-09-04T01:20:00.000Z',
          safetyEquipmentCodes: [],
        },
        approval: {
          completed: false,
          actorId: null,
          actorPosition: null,
          capacity: null,
          principalManagerUserId: null,
          principalPosition: null,
          authorizationId: null,
          actingAssignmentId: null,
          statement: null,
          approvedAt: null,
        },
      },
    };
    currentTasks = [
      {
        id: 'area-task-id',
        permitId: permit.id,
        permitVersion: permit.version,
        type: 'AREA_APPROVE_AND_ISSUE',
        label: 'Setujui dan terbitkan PTW',
        requiredRole: 'AreaOwnerManager',
        status: 'PENDING',
        permitNumber: permit.permitNumber ?? null,
        permitTitle: permit.draft.title,
        locationId: permit.draft.locationId,
        createdAt: permit.createdAt,
        completedAt: null,
      },
    ];

    const fixture = TestBed.createComponent(PermitDetail);
    fixture.detectChanges();

    const warning = fixture.nativeElement.querySelector(
      '.approval-prerequisite',
    ) as HTMLElement | null;
    const approveButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((button) => button.textContent?.includes('Setujui dan terbitkan'));

    expect(warning?.textContent).toContain('Bagian 5 belum lengkap');
    expect(warning?.textContent).toContain('Minta revisi');
    expect(approveButton?.disabled).toBe(true);
  });

  it('shows a renewal failure beside the renewal form instead of at the top', () => {
    const fixture = TestBed.createComponent(PermitDetail);
    fixture.detectChanges();

    const openButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((button) => button.textContent?.includes('Ajukan perpanjangan'));
    openButton?.click();
    fixture.detectChanges();

    const renewalForm = (fixture.componentInstance as unknown as { renewalForm: FormGroup })
      .renewalForm;
    renewalForm.setValue({
      validFrom: '2026-09-04T19:00',
      validUntil: '2026-09-05T19:00',
    });
    fixture.detectChanges();

    const submit = fixture.nativeElement.querySelector(
      '.renewal-actions button[type="submit"]',
    ) as HTMLButtonElement | null;
    submit?.click();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('.renewal-error[role="alert"]');
    expect(alert?.textContent).toContain('PTW asal sudah melewati masa berlaku.');
    expect(fixture.nativeElement.querySelector('.workflow-error')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Masa berlaku renewal');
  });

  it('reloads the detail when navigation changes the permit id', () => {
    const fixture = TestBed.createComponent(PermitDetail);
    fixture.detectChanges();
    expect(requestedPermitIds).toEqual(['permit-id']);

    routeParamMap.next(convertToParamMap({ id: 'source-permit-id' }));
    fixture.detectChanges();

    expect(requestedPermitIds).toEqual(['permit-id', 'source-permit-id']);
    expect(fixture.nativeElement.querySelector('h1')?.textContent).toContain(
      'PTW-source-permit-id',
    );
  });
});
