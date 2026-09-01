import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface LocationOption {
  id: string;
  code: string;
  name: string;
}

export interface PagedLocationOptions {
  items: LocationOption[];
  count: number;
}

@Injectable({ providedIn: 'root' })
export class LocationApi {
  constructor(private readonly http: HttpClient) {}

  list(): Observable<PagedLocationOptions> {
    return this.http.get<PagedLocationOptions>('/api/v1/locations');
  }
}
