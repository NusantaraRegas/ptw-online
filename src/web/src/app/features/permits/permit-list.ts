import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Permit, PermitApi } from '../../core/permit-api';

@Component({
  selector: 'app-permit-list',
  imports: [RouterLink, DatePipe],
  template: ` <div class="page-title">
      <div>
        <p class="eyebrow">Permit Management</p>
        <h1>PTW Saya</h1>
        <p class="subtitle">Draft, izin aktif, dan riwayat pekerjaan Anda.</p>
      </div>
      <a class="primary-button" routerLink="/permits/new">＋ Buat PTW baru</a>
    </div>
    <section class="card list-card">
      <div class="toolbar">
        <strong>{{ permits().length }} PTW</strong
        ><span>Diurutkan berdasarkan aktivitas terbaru</span>
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
          <span class="badge">{{ permit.status }}</span
          ><time>{{ permit.updatedAt | date: 'dd MMM yyyy, HH:mm' : 'Asia/Jakarta' }} WIB</time>
        </a>
      } @empty {
        @if (!loading() && !error()) {
          <div class="state">Belum ada PTW. Buat draft pertama Anda.</div>
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
        padding: 0 20px;
        display: flex;
        align-items: center;
        gap: 10px;
        border-bottom: 1px solid #e5eaeb;
      }
      .toolbar strong {
        font-size: 12px;
      }
      .toolbar span {
        color: #96a4a8;
        font-size: 9px;
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
        color: var(--nr-lime-dark);
        background: var(--nr-lime-soft);
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
  private readonly destroyRef = inject(DestroyRef);
  protected readonly permits = signal<Permit[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  constructor() {
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.permits.set(result.items);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('API belum tersedia. Pastikan SQL Server dan Ptw.Api sedang berjalan.');
          this.loading.set(false);
        },
      });
  }
}
