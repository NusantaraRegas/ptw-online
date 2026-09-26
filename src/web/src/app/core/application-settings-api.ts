import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface DemoModeSetting {
  enabled: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
  eTag: string;
}

export interface AuthenticationOptions {
  demoModeEnabled: boolean;
}

export interface UserGuideSetting {
  available: boolean;
  fileName: string | null;
  sizeBytes: number;
  sha256: string | null;
  version: number;
  updatedAt: string | null;
  updatedBy: string | null;
  eTag: string;
}

@Injectable({ providedIn: 'root' })
export class ApplicationSettingsApi {
  constructor(private readonly http: HttpClient) {}

  publicOptions(): Observable<AuthenticationOptions> {
    return this.http.get<AuthenticationOptions>('/api/v1/auth/options');
  }

  demoMode(): Observable<DemoModeSetting> {
    return this.http.get<DemoModeSetting>('/api/v1/admin/settings/demo-mode');
  }

  setDemoMode(setting: DemoModeSetting, enabled: boolean): Observable<DemoModeSetting> {
    const command = enabled ? 'enable' : 'disable';
    return this.http.post<DemoModeSetting>(
      `/api/v1/admin/settings/demo-mode/${command}`,
      {},
      {
        headers: new HttpHeaders({
          'If-Match': setting.eTag,
          'Idempotency-Key': crypto.randomUUID(),
        }),
      },
    );
  }

  userGuide(): Observable<UserGuideSetting> {
    return this.http.get<UserGuideSetting>('/api/v1/user-guide');
  }

  downloadUserGuide(): Observable<Blob> {
    return this.http.get('/api/v1/user-guide/content', { responseType: 'blob' });
  }

  replaceUserGuide(setting: UserGuideSetting, file: File): Observable<UserGuideSetting> {
    const body = new FormData();
    body.append('file', file);
    return this.http.post<UserGuideSetting>('/api/v1/admin/settings/user-guide', body, {
      headers: new HttpHeaders({
        'If-Match': setting.eTag,
        'Idempotency-Key': crypto.randomUUID(),
      }),
    });
  }
}
