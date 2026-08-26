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
  eTag: string;
}

export interface PagedPermits {
  items: Permit[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class PermitApi {
  constructor(private readonly http: HttpClient) {}
  list(): Observable<PagedPermits> {
    return this.http.get<PagedPermits>('/api/v1/permits');
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
}
