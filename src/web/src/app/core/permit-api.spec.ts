import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PermitApi, PermitDraft } from './permit-api';

describe('PermitApi', () => {
  let api: PermitApi;
  let http: HttpTestingController;

  const draft: PermitDraft = {
    title: 'Perawatan pompa',
    description: 'Perawatan terencana',
    locationId: 'AREA-A',
    sponsorId: 'sponsor.demo',
    performingAuthority: 'Pelaksana Demo',
    company: 'PT Mitra',
    permitClass: 'ColdWork',
    riskLevel: 'Medium',
    validFrom: '2026-08-26T01:00:00.000Z',
    validUntil: '2026-08-26T09:00:00.000Z',
    hazards: ['Energi tersimpan'],
    controls: ['Isolasi energi'],
    requiredDocumentCodes: [],
    workTypeCodes: ['COLD_MECHANICAL'],
    headerClassificationCodes: ['COLD_LOW_RISK'],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PermitApi, provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PermitApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads one permit by id', () => {
    api.get('permit-id').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id');
    expect(request.request.method).toBe('GET');
    request.flush({});
  });

  it('loads workflow tasks scoped by the server', () => {
    api.listTasks().subscribe();
    const request = http.expectOne('/api/v1/tasks');
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('loads the controlled work types used by the form and PDF', () => {
    api.listWorkTypes().subscribe();
    const request = http.expectOne('/api/v1/reference-data/work-types');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('loads the controlled header classifications used by the form and PDF', () => {
    api.listHeaderClassifications().subscribe();
    const request = http.expectOne('/api/v1/reference-data/header-classifications');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('loads the mandatory upload document catalog', () => {
    api.listMandatoryDocuments().subscribe();
    const request = http.expectOne('/api/v1/reference-data/mandatory-documents');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('sends If-Match when updating a draft', () => {
    api.updateDraft('permit-id', draft, '"etag-value"').subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/draft');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.body).toEqual(draft);
    request.flush({});
  });

  it('requests area-owner renewal review with concurrency and idempotency headers', () => {
    const renewal = {
      validFrom: '2026-08-26T09:00:00.000Z',
      validUntil: '2026-08-26T17:00:00.000Z',
      printPackageId: 'package-id',
      signedFieldCopyAttachmentIds: ['attachment-id'],
      continuationStatement: 'Pekerjaan perlu dilanjutkan.',
      allPagesReviewed: true,
      readableAndCompleteAcknowledged: true,
    };
    api.requestRenewal('permit-id', '"etag-value"', renewal).subscribe();
    const request = http.expectOne('/api/v1/permits/permit-id/renew');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual(renewal);
    request.flush({});
  });

  it('sends renewal approval to the dedicated area-owner task endpoint', () => {
    const body = {
      statement: 'Hardcopy terverifikasi.',
      fieldVerificationConfirmed: true,
      evidenceReadable: true,
    };
    api.approveRenewal('task-id', '"etag-value"', body).subscribe();

    const request = http.expectOne('/api/v1/renewal-tasks/task-id/approve');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('sends closure request with exact field-copy references', () => {
    const body = {
      printPackageId: 'package-id',
      signedFieldCopyAttachmentIds: ['attachment-id'],
      completionStatement: 'Pekerjaan selesai.',
      allPagesReviewed: true,
      readableAndCompleteAcknowledged: true,
    };
    api.requestClosure('permit-id', '"etag-value"', body).subscribe();

    const request = http.expectOne('/api/v1/permits/permit-id/closure-requests');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('resubmits closure evidence through the dedicated sponsor command', () => {
    const body = {
      printPackageId: 'package-id',
      signedFieldCopyAttachmentIds: ['replacement-attachment-id'],
      completionStatement: 'Pekerjaan telah selesai setelah tindak lanjut.',
      allPagesReviewed: true,
      readableAndCompleteAcknowledged: true,
    };
    api.resubmitClosure('permit-id', '"etag-value"', body).subscribe();

    const request = http.expectOne('/api/v1/permits/permit-id/closure-requests/resubmit');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('sends the complete Bagian 10 verification to the dedicated closure task', () => {
    const body = {
      statement: 'Bagian 10 dan hardcopy telah diverifikasi.',
      officerName: 'Officer Operasi',
      workAreaInspectedAndClean: true,
      workCompleted: true,
      managerAgreesWorkCompleted: true,
      inhibitedSystemsRestored: true,
      areaHandedBackAndSafeguardsRestored: true,
      evidenceReadable: true,
    };
    api.close('closure-task-id', '"etag-value"', body).subscribe();

    const request = http.expectOne('/api/v1/closure-tasks/closure-task-id/close');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('loads scoped activity with pagination', () => {
    api.listActivity('permit-id', 10, 5).subscribe();
    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/v1/permits/permit-id/activity' &&
        candidate.params.get('offset') === '10' &&
        candidate.params.get('limit') === '5',
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('loads immutable version history with pagination', () => {
    api.listVersions('permit-id', 0, 10).subscribe();
    const request = http.expectOne(
      (candidate) =>
        candidate.url === '/api/v1/permits/permit-id/versions' &&
        candidate.params.get('offset') === '0' &&
        candidate.params.get('limit') === '10',
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], count: 0 });
  });

  it('sends explicit validation command with concurrency and idempotency headers', () => {
    api
      .validate('task-id', '"etag-value"', {
        statement: 'Persyaratan HSE sesuai.',
        safetyEquipmentCodes: ['SAFETY_FIRE_EXTINGUISHER', 'SAFETY_LOTO'],
      })
      .subscribe();
    const request = http.expectOne('/api/v1/tasks/task-id/validate');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual({
      statement: 'Persyaratan HSE sesuai.',
      safetyEquipmentCodes: ['SAFETY_FIRE_EXTINGUISHER', 'SAFETY_LOTO'],
    });
    request.flush({});
  });

  it('loads the controlled Bagian 5 safety-equipment catalogue', () => {
    api.listSafetyEquipment().subscribe();
    const request = http.expectOne('/api/v1/reference-data/safety-equipment');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('loads the controlled Bagian 7 operational-condition catalogue', () => {
    api.listOperationalConditions().subscribe();
    const request = http.expectOne('/api/v1/reference-data/operational-conditions');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('submits Senior Officer Bagian 7 evidence through an explicit task command', () => {
    const body = {
      statement: 'Kondisi operasi telah diperiksa.',
      conditionCodes: ['OPS_DEPRESSURIZED'],
      otherConditionDetail: null,
      conditionsReviewed: true,
    };
    api.reviewAreaOperations('task-id', '"etag-value"', body).subscribe();
    const request = http.expectOne('/api/v1/tasks/task-id/review-area-operations');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('sends an explicit revision command with a mandatory reason', () => {
    api.requestRevision('task-id', '"etag-value"', 'Dokumen perlu diperbaiki.').subscribe();
    const request = http.expectOne('/api/v1/tasks/task-id/revision');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual({ reason: 'Dokumen perlu diperbaiki.' });
    request.flush({});
  });

  it('sends an explicit reject command instead of a generic status update', () => {
    api.reject('task-id', '"etag-value"', 'Persyaratan tidak diterima.').subscribe();
    const request = http.expectOne('/api/v1/tasks/task-id/reject');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"etag-value"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(request.request.body).toEqual({ reason: 'Persyaratan tidak diterima.' });
    request.flush({});
  });

  it('uses one atomic approve-and-issue task command', () => {
    api
      .approveAndIssue('task-id', '"etag-value"', {
        statement: 'Disetujui dan diterbitkan.',
        actingAssignmentId: null,
      })
      .subscribe();
    const request = http.expectOne('/api/v1/tasks/task-id/approve-and-issue');
    expect(request.request.body).toEqual({
      statement: 'Disetujui dan diterbitkan.',
      actingAssignmentId: null,
    });
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({});
  });

  it('suspends immediately and resolves through explicit commands', () => {
    api.suspend('permit-id', '"etag-value"', 'Kondisi lapangan berubah.').subscribe();
    const suspension = http.expectOne('/api/v1/permits/permit-id/suspensions');
    expect(suspension.request.body).toEqual({ reason: 'Kondisi lapangan berubah.' });
    suspension.flush({});

    api.resolveSuspension('permit-id', '"etag-next"', 'Kondisi aman kembali.').subscribe();
    const resolution = http.expectOne('/api/v1/permits/permit-id/suspensions/resolve');
    expect(resolution.request.body).toEqual({ resolution: 'Kondisi aman kembali.' });
    resolution.flush({});
  });
});
