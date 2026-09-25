import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface OperationsBoardMetrics {
  total: number;
  underValidation: number;
  revisionRequired: number;
  awaitingAreaApproval: number;
  issued: number;
  suspended: number;
  expiringSoon: number;
  closureRequested: number;
  closed: number;
  rejected: number;
  cancelled: number;
  expired: number;
}

export interface OperationsBoardItem {
  id: string;
  permitNumber: string | null;
  title: string;
  company: string;
  locationId: string;
  permitClass: string;
  status:
    | 'UNDER_VALIDATION'
    | 'REVISION_REQUIRED'
    | 'AWAITING_AREA_APPROVAL'
    | 'ISSUED'
    | 'SUSPENDED'
    | 'CLOSURE_REQUESTED'
    | 'CLOSED'
    | 'REJECTED'
    | 'CANCELLED'
    | 'EXPIRED';
  validFrom: string;
  validUntil: string;
  updatedAt: string;
  suspensionReason: string | null;
}

export interface OperationsBoardResponse {
  metrics: OperationsBoardMetrics;
  items: OperationsBoardItem[];
  count: number;
  generatedAt: string;
}

export interface OperationsBoardFilters {
  status?: string;
  locationId?: string;
  permitClass?: string;
  search?: string;
  offset: number;
  limit: number;
}

@Injectable({ providedIn: 'root' })
export class OperationsApi {
  constructor(private readonly http: HttpClient) {}

  list(filters: OperationsBoardFilters): Observable<OperationsBoardResponse> {
    let params = new HttpParams().set('offset', filters.offset).set('limit', filters.limit);
    for (const [key, value] of Object.entries(filters)) {
      if (key !== 'offset' && key !== 'limit' && value) {
        params = params.set(key, value);
      }
    }
    return this.http.get<OperationsBoardResponse>('/api/v1/operations', { params });
  }
}
