import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PermitAttachment {
  id: string;
  permitId: string;
  addedInVersion: number;
  removedInVersion: number | null;
  fileName: string;
  sizeBytes: number;
  mediaType: string;
  sha256: string;
  scanStatus: string;
  uploadedBy: string;
  uploadedAt: string;
}

export interface PermitAttachmentMutation {
  attachment: PermitAttachment;
  permitVersion: number;
  eTag: string;
}

@Injectable({ providedIn: 'root' })
export class PermitAttachmentApi {
  constructor(private readonly http: HttpClient) {}

  list(permitId: string): Observable<PermitAttachment[]> {
    return this.http.get<PermitAttachment[]>(`/api/v1/permits/${permitId}/attachments`);
  }

  upload(permitId: string, eTag: string, file: File): Observable<PermitAttachmentMutation> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.http.post<PermitAttachmentMutation>(
      `/api/v1/permits/${permitId}/attachments`,
      body,
      { headers: this.commandHeaders(eTag) },
    );
  }

  remove(
    permitId: string,
    attachmentId: string,
    eTag: string,
  ): Observable<PermitAttachmentMutation> {
    return this.http.post<PermitAttachmentMutation>(
      `/api/v1/permits/${permitId}/attachments/${attachmentId}/remove`,
      {},
      { headers: this.commandHeaders(eTag) },
    );
  }

  download(permitId: string, attachmentId: string): Observable<Blob> {
    return this.http.get(`/api/v1/permits/${permitId}/attachments/${attachmentId}/content`, {
      responseType: 'blob',
    });
  }

  private commandHeaders(eTag: string): HttpHeaders {
    return new HttpHeaders({
      'If-Match': eTag,
      'Idempotency-Key': crypto.randomUUID(),
    });
  }
}
