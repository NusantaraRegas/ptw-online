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
}

export interface Permit {
  id: string;
  permitNumber?: string;
  status: string;
  version: number;
  draft: PermitDraft;
  createdAt: string;
  updatedAt: string;
  activeWorkPeriodId?: string;
  suspensionReason?: string;
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
  hsse: PermitValidation;
  gasDistribution: PermitValidation;
  approvedBy: string | null;
  approvalStatement: string | null;
  approvedAt: string | null;
  suspension: PermitSuspension;
  completion: PermitCompletion;
}

export interface PermitSuspension {
  requested: boolean;
  requestedBy: string | null;
  reason: string | null;
  requestedAt: string | null;
  approved: boolean;
  approvedBy: string | null;
  approvalStatement: string | null;
  approvedAt: string | null;
}

export interface PermitCompletion {
  sponsor: PermitValidation;
  hsse: PermitValidation;
  areaOwner: PermitValidation;
}

export interface IssuePermitRequest {
  eSimiEligible: boolean;
  locationVerified: boolean;
  toolboxTalkComplete: boolean;
  personnelAcknowledged: boolean;
  ppeAndControlsVerified: boolean;
  isolationVerified: boolean;
  simopsVerified: boolean;
  gasTestSatisfied: boolean;
  hasUnresolvedSuspension: boolean;
}

export interface SubmitPermitRequest {
  eSimiEligible: boolean;
  rulesEvaluated: boolean;
  requiredDocumentsSafe: boolean;
  missingRequirements: string[];
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

  endorseHsse(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'validations/hsse/endorse', eTag, { statement });
  }

  approve(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'approve', eTag, { statement });
  }

  requestRevision(id: string, eTag: string, reason: string): Observable<Permit> {
    return this.command(id, 'request-revision', eTag, { reason });
  }

  reject(id: string, eTag: string, reason: string): Observable<Permit> {
    return this.command(id, 'reject', eTag, { reason });
  }

  requestSuspension(id: string, eTag: string, reason: string): Observable<Permit> {
    return this.command(id, 'suspensions/request', eTag, { reason });
  }

  approveSuspension(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'suspensions/approve', eTag, { statement });
  }

  declareCompletion(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'completion/declare', eTag, { statement });
  }

  confirmHsseCompletion(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'completion/confirm/hsse', eTag, { statement });
  }

  confirmAreaOwnerCompletion(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'completion/confirm/area-owner', eTag, { statement });
  }

  close(id: string, eTag: string, statement: string): Observable<Permit> {
    return this.command(id, 'close', eTag, { statement });
  }

  issue(id: string, eTag: string, readiness: IssuePermitRequest): Observable<Permit> {
    return this.command(id, 'issue', eTag, readiness);
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
}
