import { Component, input, output } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { ActivatedRoute, convertToParamMap, ParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { LocationApi } from '../../core/location-api';
import {
  Permit,
  PermitApi,
  PermitRenewalResult,
  RequestPermitRenewal,
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

  beforeEach(async () => {
    sessionStorage.setItem('ptw.development-identity', 'sponsor-admin');
    routeParamMap = new BehaviorSubject(convertToParamMap({ id: permit.id }));
    requestedPermitIds = [];

    const permitApi = {
      get: (id: string) => {
        requestedPermitIds.push(id);
        return of({ ...permit, id, permitNumber: `PTW-${id}` });
      },
      listTasks: () => of({ items: [], count: 0 }),
      listWorkTypes: () =>
        of([
          {
            permitClass: 'ColdWork',
            options: [{ code: 'COLD_MECHANICAL', label: 'Mekanikal' }],
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
