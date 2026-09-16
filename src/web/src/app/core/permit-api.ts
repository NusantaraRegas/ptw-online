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
  equipmentTag?: string | null;
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
}

export interface PermitWorkTypeCatalog {
  permitClass: string;
  options: PermitWorkTypeOption[];
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
  statement: string | null;
  completedAt: string | null;
}

export interface PermitWorkflow {
  hse: PermitValidation;
  approval: PermitApproval;
  suspension: PermitSuspension;
  closure: PermitClosure;
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
}

export interface SubmitPermitRequest {
  eSimiEligible: boolean;
  rulesEvaluated: boolean;
  requiredDocumentsSafe: boolean;
  missingRequirements: string[];
}

export interface RequestPermitRenewal {
  validFrom: string;
  validUntil: string;
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

  requestRenewal(
    id: string,
    eTag: string,
    request: RequestPermitRenewal,
  ): Observable<PermitRenewalResult> {
    return this.http.post<PermitRenewalResult>(`/api/v1/permits/${id}/renew`, request, {
      headers: new HttpHeaders({
        'If-Match': eTag,
        'Idempotency-Key': crypto.randomUUID(),
      }),
    });
  }

  validate(taskId: string, eTag: string, statement: string): Observable<Permit> {
    return this.taskCommand(taskId, 'validate', eTag, { statement });
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

  private taskCommand<TRequest>(
    taskId: string,
    command: string,
    eTag: string,
    body: TRequest,
  ): Observable<Permit> {
    return this.http.post<Permit>(`/api/v1/tasks/${taskId}/${command}`, body, {
      headers: new HttpHeaders({
        'If-Match': eTag,
        'Idempotency-Key': crypto.randomUUID(),
      }),
    });
  }
}
