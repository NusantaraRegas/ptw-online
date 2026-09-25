import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  interval,
  map,
  merge,
  of,
  Subject,
  switchMap,
  tap,
} from 'rxjs';
import {
  canMonitorAllPermits,
  CurrentIdentity,
  IdentityApi,
} from '../../core/development-identity';
import { LocationApi, LocationOption } from '../../core/location-api';
import {
  OperationsApi,
  OperationsBoardItem,
  OperationsBoardResponse,
} from '../../core/operations-api';

@Component({
  selector: 'app-operations-board',
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './operations-board.html',
  styleUrl: './operations-board.scss',
})
export class OperationsBoard {
  private readonly api = inject(OperationsApi);
  private readonly identityApi = inject(IdentityApi);
  private readonly locationApi = inject(LocationApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly form = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    locationId: new FormControl('', { nonNullable: true }),
    status: new FormControl('', { nonNullable: true }),
    permitClass: new FormControl('', { nonNullable: true }),
    pageSize: new FormControl(25, { nonNullable: true }),
  });
  protected readonly pageSizeOptions = [10, 25, 50, 100];
  protected readonly pageSize = signal(25);
  protected readonly response = signal<OperationsBoardResponse | null>(null);
  protected readonly identity = signal<CurrentIdentity | null>(null);
  protected readonly locations = signal<LocationOption[]>([]);
  protected readonly offset = signal(0);
  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly error = signal('');
  protected readonly locationError = signal('');
  protected readonly canMonitorAllStatuses = computed(() => {
    const identity = this.identity();
    return (
      identity?.roles.includes('Administrator') === true ||
      canMonitorAllPermits(identity?.roles ?? [], identity?.locationScopes ?? [])
    );
  });

  protected readonly metrics = computed(() => ({
    total: 0,
    underValidation: 0,
    revisionRequired: 0,
    awaitingAreaApproval: 0,
    issued: 0,
    suspended: 0,
    expiringSoon: 0,
    closureRequested: 0,
    closed: 0,
    rejected: 0,
    cancelled: 0,
    expired: 0,
    ...this.response()?.metrics,
  }));
  protected readonly items = computed(() => this.response()?.items ?? []);
  protected readonly count = computed(() => this.response()?.count ?? 0);
  protected readonly rangeStart = computed(() => (this.count() === 0 ? 0 : this.offset() + 1));
  protected readonly rangeEnd = computed(() =>
    Math.min(this.offset() + this.pageSize(), this.count()),
  );
  protected readonly canGoBack = computed(() => this.offset() > 0);
  protected readonly canGoForward = computed(() => this.offset() + this.pageSize() < this.count());
  protected readonly availableLocations = computed(() => {
    const options = new Map(this.locations().map((location) => [location.code, location]));
    for (const item of this.items()) {
      if (!options.has(item.locationId)) {
        options.set(item.locationId, {
          id: `operations-${item.locationId}`,
          code: item.locationId,
          name: this.locationName(item.locationId),
        });
      }
    }
    return [...options.values()].sort((left, right) => left.name.localeCompare(right.name, 'id'));
  });
  protected readonly scopeLabel = computed(() => {
    if (this.canMonitorAllStatuses()) return 'seluruh wilayah';
    const scopes = this.identity()?.locationScopes ?? [];
    if (scopes.includes('*')) return 'Seluruh wilayah yang diizinkan';
    if (scopes.length === 0) return 'Tidak ada wilayah yang ditugaskan';
    return scopes.map((scope) => this.locationName(scope)).join(', ');
  });

  constructor() {
    this.identityApi
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((identity) => this.identity.set(identity));

    this.locationApi
      .list()
      .pipe(
        catchError(() => {
          this.locationError.set('Nama lokasi gagal dimuat.');
          return of({ items: [], count: 0 });
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => this.locations.set(page.items));

    this.form.valueChanges
      .pipe(
        debounceTime(250),
        map((value) => JSON.stringify(value)),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.pageSize.set(this.form.controls.pageSize.value);
        this.offset.set(0);
        this.reload.next();
      });

    merge(this.reload, interval(30_000))
      .pipe(
        tap(() => {
          this.error.set('');
          this.response() ? this.refreshing.set(true) : this.loading.set(true);
        }),
        switchMap(() => {
          const value = this.form.getRawValue();
          return this.api
            .list({
              status: value.status || undefined,
              locationId: value.locationId || undefined,
              permitClass: value.permitClass || undefined,
              search: value.search.trim() || undefined,
              offset: this.offset(),
              limit: value.pageSize,
            })
            .pipe(
              catchError((response) => {
                this.error.set(
                  response?.error?.detail ??
                    'Papan Operasi gagal dimuat. Periksa koneksi lalu coba lagi.',
                );
                return of(null);
              }),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((response) => {
        if (response) this.response.set(response);
        this.loading.set(false);
        this.refreshing.set(false);
      });

    this.reload.next();
  }

  protected refresh(): void {
    this.reload.next();
  }

  protected previousPage(): void {
    if (!this.canGoBack()) return;
    this.offset.update((value) => Math.max(0, value - this.pageSize()));
    this.reload.next();
  }

  protected nextPage(): void {
    if (!this.canGoForward()) return;
    this.offset.update((value) => value + this.pageSize());
    this.reload.next();
  }

  protected locationName(code: string): string {
    const fallbackNames: Record<string, string> = {
      ORF: 'Onshore Receiving Facility',
      SITE_OFFICE: 'Site Office',
      WATER_BASED: 'Water-Based Activity',
    };
    return (
      this.locations().find((location) => location.code === code)?.name ??
      fallbackNames[code] ??
      'Lokasi dalam cakupan'
    );
  }

  protected statusLabel(status: OperationsBoardItem['status']): string {
    return {
      UNDER_VALIDATION: 'Validasi HSE berjalan',
      REVISION_REQUIRED: 'Perlu revisi',
      AWAITING_AREA_APPROVAL: 'Menunggu persetujuan',
      ISSUED: 'Diterbitkan',
      SUSPENDED: 'Ditangguhkan',
      CLOSURE_REQUESTED: 'Menunggu verifikasi penutupan',
      CLOSED: 'Ditutup',
      REJECTED: 'Ditolak',
      CANCELLED: 'Dibatalkan',
      EXPIRED: 'Kedaluwarsa',
    }[status];
  }

  protected permitClassLabel(permitClass: string): string {
    return (
      {
        HotWork: 'Pekerjaan Panas',
        ColdWork: 'Pekerjaan Dingin',
        ConfinedSpaceEntry: 'Memasuki Ruang Terbatas',
      }[permitClass] ?? 'Kelas izin tidak tersedia'
    );
  }

  protected isExpiringSoon(permit: OperationsBoardItem): boolean {
    const generatedAt = new Date(this.response()?.generatedAt ?? Date.now()).getTime();
    const remaining = new Date(permit.validUntil).getTime() - generatedAt;
    return remaining >= 0 && remaining <= 24 * 60 * 60 * 1000;
  }

  protected attentionLabel(permit: OperationsBoardItem): string {
    const statusAttention: Partial<Record<OperationsBoardItem['status'], string>> = {
      UNDER_VALIDATION: 'Validasi HSE sedang berlangsung',
      REVISION_REQUIRED: 'Perbaikan Sponsor diperlukan',
      AWAITING_AREA_APPROVAL: 'Menunggu keputusan Pemilik Wilayah',
      SUSPENDED: 'Pekerjaan harus dihentikan',
      CLOSURE_REQUESTED: 'Verifikasi penutupan diperlukan',
      CLOSED: 'Proses PTW telah ditutup',
      REJECTED: 'Permohonan PTW ditolak',
      CANCELLED: 'Permohonan PTW dibatalkan',
      EXPIRED: 'Masa berlaku PTW berakhir',
    };
    if (statusAttention[permit.status]) return statusAttention[permit.status]!;
    if (this.isExpiringSoon(permit)) return 'Masa berlaku segera berakhir';
    return 'PTW aktif dalam cakupan wilayah';
  }

  protected remainingLabel(permit: OperationsBoardItem): string {
    const terminalLabel: Partial<Record<OperationsBoardItem['status'], string>> = {
      UNDER_VALIDATION: 'Belum diterbitkan',
      REVISION_REQUIRED: 'Belum diterbitkan',
      AWAITING_AREA_APPROVAL: 'Belum diterbitkan',
      CLOSED: 'Selesai',
      REJECTED: 'Proses selesai',
      CANCELLED: 'Proses selesai',
      EXPIRED: 'Kedaluwarsa',
    };
    if (terminalLabel[permit.status]) return terminalLabel[permit.status]!;
    const generatedAt = new Date(this.response()?.generatedAt ?? Date.now()).getTime();
    const remaining = new Date(permit.validUntil).getTime() - generatedAt;
    if (remaining <= 0) return 'Masa berlaku telah berakhir';
    const hours = Math.ceil(remaining / (60 * 60 * 1000));
    if (hours < 24) return `Berakhir dalam ${hours} jam`;
    return `Berakhir dalam ${Math.ceil(hours / 24)} hari`;
  }
}
