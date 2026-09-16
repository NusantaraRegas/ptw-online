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
  scanEvidenceReference: string | null;
  scannedAt: string | null;
  category: PermitAttachmentCategory;
  supportingDocumentCode: string | null;
  documentNumber: string | null;
  documentRevision: string | null;
  documentDate: string | null;
  targetPermitVersion: number;
  printPackageId: string | null;
  supersedesAttachmentId: string | null;
  uploadedBy: string;
  uploadedAt: string;
}

export type PermitAttachmentCategory = 'SUPPORTING' | 'JSA' | 'SIGNED_FIELD_COPY';

export interface PermitAttachmentUpload {
  category: PermitAttachmentCategory;
  supportingDocumentCode?: string | null;
  documentNumber?: string | null;
  documentRevision?: string | null;
  documentDate?: string | null;
  printPackageId?: string | null;
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

  upload(
    permitId: string,
    eTag: string,
    file: File,
    metadata: PermitAttachmentUpload = { category: 'SUPPORTING' },
  ): Observable<PermitAttachmentMutation> {
    const body = new FormData();
    body.append('file', file, file.name);
    body.append('category', metadata.category);
    if (metadata.supportingDocumentCode) {
      body.append('supportingDocumentCode', metadata.supportingDocumentCode);
    }
    // JSA and signed field copies carry controlled document metadata that the server requires.
    if (metadata.documentNumber) {
      body.append('documentNumber', metadata.documentNumber);
    }
    if (metadata.documentRevision) {
      body.append('documentRevision', metadata.documentRevision);
    }
    if (metadata.documentDate) {
      body.append('documentDate', new Date(metadata.documentDate).toISOString());
    }
    if (metadata.printPackageId) {
      body.append('printPackageId', metadata.printPackageId);
    }
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
