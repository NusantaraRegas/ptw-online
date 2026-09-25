import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface UserSignature {
  id: string;
  version: number;
  mediaType: string;
  sizeBytes: number;
  sha256: string;
  uploadedAt: string;
  uploadedBy: string;
  isActive: boolean;
}

export interface UserAccount {
  subjectId: string;
  userName: string;
  displayName: string;
  position: string | null;
  department: string | null;
  isActive: boolean;
  version: number;
  createdAt: string;
  updatedAt: string;
  /** Set while the local password is locked after repeated failures; null otherwise. */
  lockedUntil: string | null;
  signature: UserSignature | null;
  eTag: string;
}

export interface CreateUser {
  subjectId: string;
  userName: string;
  displayName: string;
  position: string | null;
  department: string | null;
  password: string;
}

export interface UpdateUser {
  displayName: string;
  position: string | null;
  department: string | null;
}

interface PagedUsers {
  items: UserAccount[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class UserDirectoryApi {
  constructor(private readonly http: HttpClient) {}

  list(): Observable<PagedUsers> {
    return this.http.get<PagedUsers>('/api/v1/admin/users');
  }

  create(request: CreateUser): Observable<UserAccount> {
    return this.http.post<UserAccount>('/api/v1/admin/users', request);
  }

  update(user: UserAccount, request: UpdateUser): Observable<UserAccount> {
    return this.http.patch<UserAccount>(
      `/api/v1/admin/users/${encodeURIComponent(user.subjectId)}`,
      request,
      { headers: new HttpHeaders({ 'If-Match': user.eTag }) },
    );
  }

  setActive(user: UserAccount, isActive: boolean): Observable<UserAccount> {
    return this.http.post<UserAccount>(
      `/api/v1/admin/users/${encodeURIComponent(user.subjectId)}/active`,
      { isActive },
      { headers: new HttpHeaders({ 'If-Match': user.eTag }) },
    );
  }

  uploadSignature(user: UserAccount, file: File): Observable<UserAccount> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<UserAccount>(
      `/api/v1/admin/users/${encodeURIComponent(user.subjectId)}/signature`,
      form,
      { headers: new HttpHeaders({ 'If-Match': user.eTag }) },
    );
  }

  signatureUrl(user: UserAccount): string {
    const subjectId = encodeURIComponent(user.subjectId);
    const version = user.signature?.id ?? 'none';
    return `/api/v1/admin/users/${subjectId}/signature?v=${encodeURIComponent(version)}`;
  }
}
