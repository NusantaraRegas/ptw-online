import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export type PrintPackageRenderStatus = 'PENDING' | 'RETRYING' | 'READY' | 'FAILED';

export interface PrintPackage {
  id: string;
  permitId: string;
  permitVersion: number;
  renderStatus: PrintPackageRenderStatus;
  attempts: number;
  lastError: string | null;
  sizeBytes: number | null;
  sha256: string | null;
  printTemplateVersion: string;
  campaignAssetVersion: string;
  reference: string;
  createdAt: string;
  generatedAt: string | null;
  downloadable: boolean;
}

export interface PrintPackagePage {
  items: PrintPackage[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class PrintPackageApi {
  constructor(private readonly http: HttpClient) {}

  list(permitId: string): Observable<PrintPackagePage> {
    return this.http.get<PrintPackagePage>(`/api/v1/permits/${permitId}/print-packages`);
  }

  download(permitId: string, printPackageId: string): Observable<Blob> {
    return this.http.get(`/api/v1/permits/${permitId}/print-packages/${printPackageId}/content`, {
      responseType: 'blob',
    });
  }

  /** Draft preview. Always watermarked and never valid as closure evidence. */
  preview(permitId: string): Observable<Blob> {
    return this.http.get(`/api/v1/permits/${permitId}/print-packages/preview`, {
      responseType: 'blob',
    });
  }

  retry(permitId: string, printPackageId: string): Observable<PrintPackage> {
    return this.http.post<PrintPackage>(
      `/api/v1/permits/${permitId}/print-packages/${printPackageId}/retry`,
      {},
      { headers: new HttpHeaders({ 'Idempotency-Key': crypto.randomUUID() }) },
    );
  }
}
