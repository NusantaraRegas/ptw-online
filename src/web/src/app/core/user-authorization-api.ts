import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface UserAuthorizationDraft {
  subjectId: string;
  roleCode: string;
  actionCodes: string[];
  locationId: string | null;
  includeDescendants: boolean;
  requiredCompetencyCodes: string[];
  kind: 'DIRECT' | 'DELEGATION';
  sourceAuthorizationId: string | null;
  effectiveFrom: string;
  effectiveUntil: string | null;
}

export interface UserAuthorization extends UserAuthorizationDraft {
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

export interface PagedUserAuthorizations {
  items: UserAuthorization[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class UserAuthorizationApi {
  constructor(private readonly http: HttpClient) {}

  list(): Observable<PagedUserAuthorizations> {
    return this.http.get<PagedUserAuthorizations>('/api/v1/admin/authorizations');
  }

  create(draft: UserAuthorizationDraft): Observable<UserAuthorization> {
    return this.http.post<UserAuthorization>('/api/v1/admin/authorizations', draft);
  }

  submit(id: string, eTag: string): Observable<UserAuthorization> {
    return this.command(id, 'submit', eTag);
  }

  approve(id: string, eTag: string): Observable<UserAuthorization> {
    return this.command(id, 'approve', eTag);
  }

  private command(id: string, command: string, eTag: string): Observable<UserAuthorization> {
    return this.http.post<UserAuthorization>(
      `/api/v1/admin/authorizations/${id}/${command}`,
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
