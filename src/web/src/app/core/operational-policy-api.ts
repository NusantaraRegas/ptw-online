import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PolicyRequirement {
  code: string;
  label: string;
  satisfied: boolean;
  detail: string;
}

export interface OperationalPolicyReadiness {
  enforcementEnabled: boolean;
  readyForActivation: boolean;
  mode: 'PREPARATION' | 'MASTER_AUTHORIZATION';
  policyVersion: string;
  requirements: PolicyRequirement[];
  evaluatedAt: string;
}

export interface PolicySimulationRequest {
  subjectId: string;
  actionCode: string;
  locationCode: string;
  competencyCodes: string[];
  evaluatedAt: string | null;
}

export interface PolicySimulationAssignment {
  id: string;
  roleCode: string;
  kind: 'DIRECT' | 'DELEGATION';
  locationId: string | null;
  includeDescendants: boolean;
  requiredCompetencyCodes: string[];
  effectiveFrom: string;
  effectiveUntil: string | null;
}

export interface PolicySimulationCheck {
  code: string;
  label: string;
  passed: boolean;
  detail: string;
}

export interface PolicySimulationResult {
  allowed: boolean;
  outcome: 'ALLOW' | 'DENY';
  code: string;
  summary: string;
  isAuthoritative: boolean;
  enforcementEnabled: boolean;
  policyVersion: string;
  evaluatedAt: string;
  location: { id: string; code: string; name: string; parentId: string | null } | null;
  assignments: PolicySimulationAssignment[];
  requiredCompetencyCodes: string[];
  missingCompetencyCodes: string[];
  checks: PolicySimulationCheck[];
}

@Injectable({ providedIn: 'root' })
export class OperationalPolicyApi {
  constructor(private readonly http: HttpClient) {}

  readiness(): Observable<OperationalPolicyReadiness> {
    return this.http.get<OperationalPolicyReadiness>('/api/v1/admin/policy-readiness');
  }

  simulate(request: PolicySimulationRequest): Observable<PolicySimulationResult> {
    return this.http.post<PolicySimulationResult>('/api/v1/admin/policy-simulations', request);
  }
}
