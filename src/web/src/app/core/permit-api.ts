import { HttpClient } from '@angular/common/http';
import { HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PermitDraft {
  title: string;
  description: string;
  locationId: string;
  sponsorId: string;
  performingAuthority: string;
  company: string;
  permitClass: string;
  riskLevel: string;
  validFrom: string;
  validUntil: string;
  eSimiExternalId?: string | null;
  eSimiNumber?: string | null;
  hazards: string[];
  controls: string[];
  requiredDocumentCodes: string[];
  submitterType?: 'CONTRACTOR' | 'USER_SPONSOR';
  workTypeCode?: string | null;
  workTypeCodes?: string[];
  otherWorkTypeDescription?: string | null;
  equipmentTag?: string | null;
  equipmentName?: string | null;
  workOrderNumber?: string | null;
  additionalHazardReference?: string | null;
  headerClassificationCodes?: string[];
  plantArea?: string | null;
  clsrApplicable?: boolean;
  simopsDeclaration?: string | null;
  safetyEquipmentCodes?: string[];
  isolationPrecautionCodes?: string[];
  jsaDocumentNumber?: string | null;
  jsaRevision?: string | null;
  jsaDate?: string | null;
}

export interface PermitWorkTypeOption {
  code: string;
  label: string;
  requiresDetail: boolean;
}

export interface PermitHeaderClassificationOption {
  code: string;
  label: string;
}

export interface PermitHeaderClassificationCatalog {
  permitClass: string;
  selectionMode: 'NONE' | 'SINGLE' | 'MULTIPLE';
  options: PermitHeaderClassificationOption[];
}

export interface PermitWorkTypeCatalog {
  permitClass: string;
  options: PermitWorkTypeOption[];
}

export interface PermitSafetyEquipmentOption {
  code: string;
  label: string;
}

export interface PermitSafetyEquipmentCatalog {
  permitClass: string;
  options: PermitSafetyEquipmentOption[];
}

export interface PermitOperationalConditionOption {
  code: string;
  label: string;
  templateIndex: number;
  parentCode: string | null;
  requiresDetail: boolean;
}

export interface PermitSupportingDocumentOption {
  code: string;
  label: string;
  templateColumn: number;
  templateIndex: number;
  required: boolean;
  requiresMetadata: boolean;
}

export interface PermitMandatoryDocumentOption {
  code: string;
  label: string;
  uploadCategory: 'JSA' | 'SUPPORTING';
  requiresMetadata: boolean;
}

export interface Permit {
  id: string;
  permitNumber?: string;
  status: string;
  version: number;
  draft: PermitDraft;
  createdAt: string;
  updatedAt: string;
  suspensionReason?: string;
  renewedFromPermitId?: string | null;
  renewalPermitId?: string | null;
  workflow: PermitWorkflow;
  eTag: string;
}

export interface PermitValidation {
  code: string;
  label: string;
  completed: boolean;
  actorId: string | null;
  actorName: string | null;
  statement: string | null;
  completedAt: string | null;
  safetyEquipmentCodes: string[];
}

export interface PermitWorkflow {
  hse: PermitValidation;
  areaOperations: AreaOperationsReview;
  approval: PermitApproval;
  suspension: PermitSuspension;
  renewal?: PermitRenewalWorkflow;
  closure: PermitClosure;
}

export interface AreaOperationsReview {
  completed: boolean;
  actorId: string | null;
  actorName: string | null;
  actorPosition: string | null;
  authorizationId: string | null;
  conditionCodes: string[];
  otherConditionDetail: string | null;
  statement: string | null;
  reviewedAt: string | null;
}

export interface PermitRenewalWorkflow {
  requested: boolean;
  status: 'PENDING' | 'REVISION_REQUIRED' | 'APPROVED' | 'REJECTED' | null;
  printPackageId: string | null;
  signedFieldCopyAttachmentIds: string[];
  requestedBy: string | null;
  continuationStatement: string | null;
  validFrom: string | null;
  validUntil: string | null;
  requestedAt: string | null;
  revision: number;
  replacementReason: string | null;
  decidedBy: string | null;
  decisionStatement: string | null;
  decidedAt: string | null;
}

export interface PermitApproval {
  completed: boolean;
  actorId: string | null;
  actorPosition: string | null;
  capacity: 'MANAGER' | 'ACTING_FOR_MANAGER' | null;
  principalManagerUserId: string | null;
  principalPosition: string | null;
  authorizationId: string | null;
  actingAssignmentId: string | null;
  statement: string | null;
  approvedAt: string | null;
  actorName?: string | null;
}

export interface PermitSuspension {
  suspended: boolean;
  suspendedBy: string | null;
  reason: string | null;
  suspendedAt: string | null;
  resolvedBy: string | null;
  resolution: string | null;
  resolvedAt: string | null;
}

export interface PermitClosure {
  requested: boolean;
  printPackageId: string | null;
  signedFieldCopyAttachmentIds: string[];
  requestedBy: string | null;
  completionStatement: string | null;
  requestedAt: string | null;
  revision: number;
  replacementReason: string | null;
  closed: boolean;
  closedBy: string | null;
  closeStatement: string | null;
  closedAt: string | null;
  officerName?: string | null;
  workAreaInspectedAndClean?: boolean;
  workCompleted?: boolean;
  managerAgreesWorkCompleted?: boolean;
  inhibitedSystemsRestored?: boolean;
  areaHandedBackAndSafeguardsRestored?: boolean;
  evidenceReadable?: boolean;
}

export interface SubmitPermitRequest {
  eSimiEligible: boolean;
  rulesEvaluated: boolean;
  requiredDocumentsSafe: boolean;
  missingRequirements: string[];
}

export interface ValidateSubmissionRequest {
  statement: string;
  safetyEquipmentCodes: string[];
}

export interface RequestPermitRenewal {
  validFrom: string;
  validUntil: string;
  printPackageId: string;
  signedFieldCopyAttachmentIds: string[];
  continuationStatement: string;
  allPagesReviewed: boolean;
  readableAndCompleteAcknowledged: boolean;
}

export interface RequestPermitClosure {
  printPackageId: string;
  signedFieldCopyAttachmentIds: string[];
  completionStatement: string;
  allPagesReviewed: boolean;
  readableAndCompleteAcknowledged: boolean;
}

export interface PermitRenewalResult {
  sourcePermitVersion: number;
  sourceETag: string;
  renewal: Permit;
}

export interface PagedPermits {
  items: Permit[];
  count: number;
}

export interface PermitTask {
  id: string;
  permitId: string;
  permitVersion: number;
  type: string;
  label: string;
  requiredRole: string;
  status: string;
  permitNumber: string | null;
  permitTitle: string;
  locationId: string;
  createdAt: string;
  completedAt: string | null;
}

export interface PagedPermitTasks {
  items: PermitTask[];
  count: number;
}

export interface PermitActivity {
  sequence: number;
  eventType: string;
  actorId: string;
  occurredAt: string;
  payload: Record<string, unknown>;
  correlationId: string;
}

export interface PermitVersion {
  version: number;
  snapshot: PermitDraft;
  contentHash: string;
  createdAt: string;
  createdBy: string;
}

export interface PagedHistory<T> {
  items: T[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class PermitApi {
  constructor(private readonly http: HttpClient) {}
  list(): Observable<PagedPermits> {
    return this.http.get<PagedPermits>('/api/v1/permits');
  }

  listTasks(): Observable<PagedPermitTasks> {
    return this.http.get<PagedPermitTasks>('/api/v1/tasks');
  }

  listWorkTypes(): Observable<PermitWorkTypeCatalog[]> {
    return this.http.get<PermitWorkTypeCatalog[]>('/api/v1/reference-data/work-types');
  }

  listHeaderClassifications(): Observable<PermitHeaderClassificationCatalog[]> {
    return this.http.get<PermitHeaderClassificationCatalog[]>(
      '/api/v1/reference-data/header-classifications',
    );
  }

  listSafetyEquipment(): Observable<PermitSafetyEquipmentCatalog[]> {
    return this.http.get<PermitSafetyEquipmentCatalog[]>('/api/v1/reference-data/safety-equipment');
  }

  listOperationalConditions(): Observable<PermitOperationalConditionOption[]> {
    return this.http.get<PermitOperationalConditionOption[]>(
      '/api/v1/reference-data/operational-conditions',
    );
  }

  listSupportingDocuments(): Observable<PermitSupportingDocumentOption[]> {
    return this.http.get<PermitSupportingDocumentOption[]>(
      '/api/v1/reference-data/supporting-documents',
    );
  }

  listMandatoryDocuments(): Observable<PermitMandatoryDocumentOption[]> {
    return this.http.get<PermitMandatoryDocumentOption[]>(
      '/api/v1/reference-data/mandatory-documents',
    );
  }

  create(draft: PermitDraft): Observable<Permit> {
    return this.http.post<Permit>('/api/v1/permits', draft);
  }

  get(id: string): Observable<Permit> {
    return this.http.get<Permit>(`/api/v1/permits/${id}`);
  }

  updateDraft(id: string, draft: PermitDraft, eTag: string): Observable<Permit> {
    return this.http.patch<Permit>(`/api/v1/permits/${id}/draft`, draft, {
      headers: new HttpHeaders({ 'If-Match': eTag }),
    });
  }

  submit(id: string, eTag: string, readiness: SubmitPermitRequest): Observable<Permit> {
    return this.command(id, 'submit', eTag, readiness);
  }

  requestRenewal(id: string, eTag: string, request: RequestPermitRenewal): Observable<Permit> {
    return this.http.post<Permit>(`/api/v1/permits/${id}/renew`, request, {
      headers: new HttpHeaders({
        'If-Match': eTag,
        'Idempotency-Key': crypto.randomUUID(),
      }),
    });
  }

  approveRenewal(
    taskId: string,
    eTag: string,
    request: {
      statement: string;
      fieldVerificationConfirmed: boolean;
      evidenceReadable: boolean;
    },
  ): Observable<PermitRenewalResult> {
    return this.taskCommand<
      {
        statement: string;
        fieldVerificationConfirmed: boolean;
        evidenceReadable: boolean;
      },
      PermitRenewalResult
    >(taskId, 'approve', eTag, request, '/api/v1/renewal-tasks');
  }

  requestRenewalEvidence(taskId: string, eTag: string, reason: string): Observable<Permit> {
    return this.taskCommand(taskId, 'request-evidence', eTag, { reason }, '/api/v1/renewal-tasks');
  }

  rejectRenewal(taskId: string, eTag: string, reason: string): Observable<Permit> {
    return this.taskCommand(taskId, 'reject', eTag, { reason }, '/api/v1/renewal-tasks');
  }

  requestClosure(id: string, eTag: string, request: RequestPermitClosure): Observable<Permit> {
    return this.command(id, 'closure-requests', eTag, request);
  }

  resubmitClosure(id: string, eTag: string, request: RequestPermitClosure): Observable<Permit> {
    return this.command(id, 'closure-requests/resubmit', eTag, request);
  }

  requestClosureEvidence(taskId: string, eTag: string, reason: string): Observable<Permit> {
    return this.taskCommand(taskId, 'request-evidence', eTag, { reason }, '/api/v1/closure-tasks');
  }

  close(
    taskId: string,
    eTag: string,
    request: {
      statement: string;
      officerName: string;
      workAreaInspectedAndClean: boolean;
      workCompleted: boolean;
      managerAgreesWorkCompleted: boolean;
      inhibitedSystemsRestored: boolean;
      areaHandedBackAndSafeguardsRestored: boolean;
      evidenceReadable: boolean;
    },
  ): Observable<Permit> {
    return this.taskCommand(taskId, 'close', eTag, request, '/api/v1/closure-tasks');
  }

  validate(taskId: string, eTag: string, request: ValidateSubmissionRequest): Observable<Permit> {
    return this.taskCommand(taskId, 'validate', eTag, request);
  }

  reviewAreaOperations(
    taskId: string,
    eTag: string,
    request: {
      statement: string;
      conditionCodes: string[];
      otherConditionDetail: string | null;
      conditionsReviewed: boolean;
    },
  ): Observable<Permit> {
    return this.taskCommand(taskId, 'review-area-operations', eTag, request);
  }

  approveAndIssue(
    taskId: string,
    eTag: string,
    request: {
      statement: string;
      actingAssignmentId: string | null;
    },
  ): Observable<Permit> {
    return this.taskCommand(taskId, 'approve-and-issue', eTag, request);
  }

  requestRevision(taskId: string, eTag: string, reason: string): Observable<Permit> {
    return this.taskCommand(taskId, 'revision', eTag, { reason });
  }

  reject(taskId: string, eTag: string, reason: string): Observable<Permit> {
    return this.taskCommand(taskId, 'reject', eTag, { reason });
  }

  suspend(id: string, eTag: string, reason: string): Observable<Permit> {
    return this.command(id, 'suspensions', eTag, { reason });
  }

  resolveSuspension(id: string, eTag: string, resolution: string): Observable<Permit> {
    return this.command(id, 'suspensions/resolve', eTag, { resolution });
  }

  listActivity(id: string, offset = 0, limit = 10): Observable<PagedHistory<PermitActivity>> {
    return this.http.get<PagedHistory<PermitActivity>>(`/api/v1/permits/${id}/activity`, {
      params: { offset, limit },
    });
  }

  listVersions(id: string, offset = 0, limit = 10): Observable<PagedHistory<PermitVersion>> {
    return this.http.get<PagedHistory<PermitVersion>>(`/api/v1/permits/${id}/versions`, {
      params: { offset, limit },
    });
  }

  private command<TRequest>(
    id: string,
    command: string,
    eTag: string,
    body: TRequest,
  ): Observable<Permit> {
    return this.http.post<Permit>(`/api/v1/permits/${id}/${command}`, body, {
      headers: new HttpHeaders({
        'If-Match': eTag,
        'Idempotency-Key': crypto.randomUUID(),
      }),
    });
  }

  private taskCommand<TRequest, TResponse = Permit>(
    taskId: string,
    command: string,
    eTag: string,
    body: TRequest,
    basePath = '/api/v1/tasks',
  ): Observable<TResponse> {
    return this.http.post<TResponse>(`${basePath}/${taskId}/${command}`, body, {
      headers: new HttpHeaders({
        'If-Match': eTag,
        'Idempotency-Key': crypto.randomUUID(),
      }),
    });
  }
}
