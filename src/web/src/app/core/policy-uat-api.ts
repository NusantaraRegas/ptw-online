import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PolicySimulationResult } from './operational-policy-api';

export interface PolicyUatScenario {
  caseCode: string;
  description: string;
  subjectId: string;
  actionCode: string;
  locationCode: string;
  competencyCodes: string[];
  evaluatedAt: string | null;
  expectedOutcome: 'ALLOW' | 'DENY';
  expectedCode: string | null;
}

export interface PolicyUatSuiteDraft {
  suiteKey: string;
  name: string;
  policyVersion: string;
  scenarios: PolicyUatScenario[];
}

export interface PolicyUatCoverage {
  scenarioCount: number;
  expectedAllowCount: number;
  expectedDenyCount: number;
  actualAllowCount: number;
  actualDenyCount: number;
  matchedCount: number;
  distinctSubjectCount: number;
  distinctActionCount: number;
  distinctLocationCount: number;
  distinctRoleCount: number;
  distinctCompetencyCount: number;
  temporalScenarioCount: number;
}

export interface PolicyUatRunSummary {
  id: string;
  passed: boolean;
  matchedCount: number;
  scenarioCount: number;
  reportHash: string;
  executedAt: string;
  executedBy: string;
}

export interface PolicyUatSuite {
  id: string;
  suiteKey: string;
  name: string;
  policyVersion: string;
  version: number;
  scenarios: PolicyUatScenario[];
  contentHash: string;
  createdAt: string;
  createdBy: string;
  latestRun: PolicyUatRunSummary | null;
}

export interface PolicyUatScenarioResult {
  caseCode: string;
  expectedOutcome: 'ALLOW' | 'DENY';
  expectedCode: string | null;
  actualOutcome: 'ALLOW' | 'DENY';
  actualCode: string;
  matched: boolean;
  actual: PolicySimulationResult;
}

export interface PolicyUatRun {
  id: string;
  suiteId: string;
  suiteKey: string;
  suiteVersion: number;
  policyVersion: string;
  suiteContentHash: string;
  passed: boolean;
  coverage: PolicyUatCoverage;
  results: PolicyUatScenarioResult[];
  reportHash: string;
  executedAt: string;
  executedBy: string;
}

interface PagedResponse<T> {
  items: T[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class PolicyUatApi {
  constructor(private readonly http: HttpClient) {}

  listSuites(): Observable<PagedResponse<PolicyUatSuite>> {
    return this.http.get<PagedResponse<PolicyUatSuite>>('/api/v1/admin/policy-uat-suites');
  }

  createSuite(draft: PolicyUatSuiteDraft): Observable<PolicyUatSuite> {
    return this.http.post<PolicyUatSuite>('/api/v1/admin/policy-uat-suites', draft, {
      headers: this.idempotencyHeaders(),
    });
  }

  runSuite(id: string): Observable<PolicyUatRun> {
    return this.http.post<PolicyUatRun>(`/api/v1/admin/policy-uat-suites/${id}/runs`, null, {
      headers: this.idempotencyHeaders(),
    });
  }

  private idempotencyHeaders(): HttpHeaders {
    return new HttpHeaders({ 'Idempotency-Key': crypto.randomUUID() });
  }
}
