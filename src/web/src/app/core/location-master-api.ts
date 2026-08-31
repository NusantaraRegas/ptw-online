import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface LocationDraft {
  code: string;
  name: string;
  parentId: string | null;
  effectiveFrom: string;
  effectiveUntil: string | null;
}

export interface LocationMaster extends LocationDraft {
  id: string;
  status: 'DRAFT' | 'PENDING_APPROVAL' | 'APPROVED';
  isEffective: boolean;
  version: number;
  makerId: string;
  checkerId: string | null;
  approvedAt: string | null;
  createdAt: string;
  updatedAt: string;
  eTag: string;
}

export interface PagedLocations {
  items: LocationMaster[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class LocationMasterApi {
  constructor(private readonly http: HttpClient) {}

  list(): Observable<PagedLocations> {
    return this.http.get<PagedLocations>('/api/v1/admin/locations');
  }

  create(draft: LocationDraft): Observable<LocationMaster> {
    return this.http.post<LocationMaster>('/api/v1/admin/locations', draft);
  }

  updateDraft(id: string, draft: LocationDraft, eTag: string): Observable<LocationMaster> {
    return this.http.patch<LocationMaster>(`/api/v1/admin/locations/${id}/draft`, draft, {
      headers: new HttpHeaders({ 'If-Match': eTag }),
    });
  }

  submit(id: string, eTag: string): Observable<LocationMaster> {
    return this.command(id, 'submit', eTag);
  }

  approve(id: string, eTag: string): Observable<LocationMaster> {
    return this.command(id, 'approve', eTag);
  }

  private command(id: string, command: string, eTag: string): Observable<LocationMaster> {
    return this.http.post<LocationMaster>(
      `/api/v1/admin/locations/${id}/${command}`,
      {},
      {
        headers: new HttpHeaders({
          'If-Match': eTag,
          'Idempotency-Key': crypto.randomUUID(),
        }),
      },
    );
  }
}
