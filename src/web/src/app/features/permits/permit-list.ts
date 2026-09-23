import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  catchError,
  debounce,
  distinctUntilChanged,
  map,
  of,
  startWith,
  switchMap,
  tap,
  timer,
} from 'rxjs';
import { Permit, PermitApi } from '../../core/permit-api';
import { CurrentIdentity, IdentityApi } from '../../core/development-identity';

@Component({
  selector: 'app-permit-list',
  imports: [RouterLink, DatePipe, ReactiveFormsModule],
  template: ` <div class="page-title">
      <div>
        <p class="eyebrow">Manajemen PTW</p>
        <h1>PTW Saya</h1>
        <p class="subtitle">Draft, izin aktif, dan riwayat pekerjaan Anda.</p>
      </div>
      @if (canCreatePermit()) {
        <a class="primary-button" routerLink="/permits/new">＋ Buat PTW baru</a>
      }
    </div>
    <section class="card list-card">
      <div class="toolbar">
        <div class="list-summary" aria-live="polite">
          <strong>{{ permits().length }} PTW</strong>
          <span>
            {{ searchTerm() ? 'hasil pencarian' : 'Diurutkan berdasarkan aktivitas terbaru' }}
          </span>
        </div>
        <label class="search-field" for="permit-search">
          <span class="visually-hidden">Cari PTW</span>
          <svg aria-hidden="true" viewBox="0 0 24 24">
            <circle cx="11" cy="11" r="7"></circle>
            <path d="m16 16 5 5"></path>
          </svg>
          <input
            id="permit-search"
            type="search"
            maxlength="100"
            autocomplete="off"
            placeholder="Cari nomor PTW, judul, perusahaan, atau lokasi"
            aria-describedby="permit-search-help"
            [formControl]="searchControl"
          />
          <span id="permit-search-help" class="visually-hidden">
            Hasil diperbarui otomatis setelah Anda berhenti mengetik.
          </span>
        </label>
      </div>
      @if (loading()) {
        <div class="state">Memuat data PTW…</div>
      }
      @if (error()) {
        <div class="state error">{{ error() }}</div>
      }
      @for (permit of permits(); track permit.id) {
        <a class="permit-item" [routerLink]="['/permits', permit.id]">
          <span class="class-code">{{
            permit.draft.permitClass === 'HotWork'
              ? 'HW'
              : permit.draft.permitClass === 'ColdWork'
                ? 'CW'
                : 'CSE'
          }}</span>
          <div>
            <strong>{{ permit.draft.title }}</strong>
            <p>
              {{ permit.permitNumber || 'DRAFT' }} · {{ permit.draft.locationId }} ·
              {{ permit.draft.company }}
            </p>
          </div>
          <span class="badge permit-status" [attr.data-status]="permit.status">
            {{ statusLabel(permit.status) }} </span
          ><time>{{ permit.updatedAt | date: 'dd MMM yyyy, HH:mm' : 'Asia/Jakarta' }} WIB</time>
        </a>
      } @empty {
        @if (!loading() && !error()) {
          <div class="state">
            {{
              searchTerm()
                ? 'Tidak ada PTW yang cocok. Coba kata pencarian lain.'
                : canCreatePermit()
                  ? 'Belum ada PTW. Buat draft pertama Anda.'
                  : 'Belum ada PTW yang tersedia untuk akun dan cakupan lokasi Anda.'
            }}
          </div>
        }
      }
    </section>`,
  styles: [
    `
      .list-card {
        overflow: hidden;
      }
      .toolbar {
        min-height: 58px;
        padding: 10px 20px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 20px;
        border-bottom: 1px solid #e5eaeb;
      }
      .list-summary {
        display: flex;
        align-items: baseline;
        gap: 10px;
        white-space: nowrap;
      }
      .list-summary strong {
        font-size: 12px;
      }
      .list-summary span {
        color: #96a4a8;
        font-size: 9px;
      }
      .search-field {
        width: min(100%, 390px);
        min-height: 38px;
        display: flex;
        align-items: center;
        gap: 9px;
        padding: 0 12px;
        border: 1px solid #cedade;
        border-radius: 10px;
        background: #fff;
        transition:
          border-color 160ms ease,
          box-shadow 160ms ease;
      }
      .search-field:focus-within {
        border-color: var(--nr-blue);
        box-shadow: 0 0 0 3px rgb(0 117 191 / 12%);
      }
      .search-field svg {
        width: 17px;
        height: 17px;
        flex: 0 0 auto;
        fill: none;
        stroke: #667a82;
        stroke-linecap: round;
        stroke-width: 2;
      }
      .search-field input {
        width: 100%;
        min-width: 0;
        padding: 0;
        border: 0;
        outline: 0;
        color: var(--nr-ink);
        background: transparent;
        font-size: 11px;
      }
      .search-field input::placeholder {
        color: #87989e;
      }
      .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
      .permit-item {
        min-height: 78px;
        padding: 0 20px;
        display: grid;
        grid-template-columns: auto 1fr auto 145px;
        align-items: center;
        gap: 15px;
        color: inherit;
        border-bottom: 1px solid #edf0f1;
        text-decoration: none;
        transition: background 160ms ease;
      }
      .permit-item:hover {
        background: #f3f8fc;
      }
      .class-code {
        width: 40px;
        height: 40px;
        display: grid;
        place-items: center;
        border-radius: 9px;
        color: var(--nr-blue-dark);
        background: var(--nr-blue-soft);
        font-size: 10px;
        font-weight: 800;
      }
      .permit-item strong {
        color: var(--nr-ink);
        font-size: 12px;
      }
      .permit-item p {
        margin: 5px 0 0;
        color: #8b9a9f;
        font-size: 9px;
      }
      .badge {
        padding: 6px 9px;
        border-radius: 11px;
        font-size: 8px;
        font-weight: 800;
      }
      time {
        color: #809196;
        font-size: 9px;
        text-align: right;
      }
      .state {
        padding: 48px;
        color: #84959a;
        text-align: center;
        font-size: 12px;
      }
      .state.error {
        color: #a44839;
        background: #fff7f5;
      }
      @media (max-width: 650px) {
        .toolbar {
          align-items: stretch;
          flex-direction: column;
          gap: 8px;
        }
        .search-field {
          width: 100%;
        }
        .permit-item {
          grid-template-columns: auto 1fr auto;
          padding: 12px;
        }
        time {
          display: none;
        }
      }
    `,
  ],
})
export class PermitList {
  private readonly api = inject(PermitApi);
  private readonly identityApi = inject(IdentityApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly permits = signal<Permit[]>([]);
  protected readonly identity = signal<CurrentIdentity | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly searchTerm = signal('');
  protected readonly canCreatePermit = computed(() =>
    (this.identity()?.roles ?? []).some((role) => ['Sponsor', 'Administrator'].includes(role)),
  );

  protected statusLabel(status: string): string {
    return (
      {
        DRAFT: 'Draft',
        REVISION_REQUIRED: 'Perlu revisi',
        UNDER_VALIDATION: 'Validasi HSE berjalan',
        AWAITING_AREA_APPROVAL: 'Menunggu approval penerbitan',
        ISSUED: 'Diterbitkan',
        SUSPENDED: 'Ditangguhkan',
        CLOSURE_REQUESTED: 'Menunggu verifikasi penutupan',
        CLOSED: 'Ditutup',
        REJECTED: 'Ditolak',
        CANCELLED: 'Dibatalkan',
        EXPIRED: 'Kedaluwarsa',
      }[status] ?? status
    );
  }

  constructor() {
    this.identityApi
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (identity) => this.identity.set(identity) });

    this.searchControl.valueChanges
      .pipe(
        startWith(this.searchControl.value),
        map((value) => value.trim()),
        debounce((value) => (value ? timer(300) : of(0))),
        distinctUntilChanged(),
        tap((search) => {
          this.searchTerm.set(search);
          this.loading.set(true);
          this.error.set('');
        }),
        switchMap((search) =>
          this.api.list(search || undefined).pipe(
            catchError(() => {
              this.error.set(
                'API belum tersedia. Pastikan SQL Server dan Ptw.Api sedang berjalan.',
              );
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          if (result) {
            this.permits.set(result.items);
          }
          this.loading.set(false);
        },
      });
  }
}
