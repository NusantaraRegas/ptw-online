import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Permit, PermitApi, PermitTask } from '../../core/permit-api';
import { DevelopmentIdentityStore } from '../../core/development-identity';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(PermitApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly identityStore = inject(DevelopmentIdentityStore);
  protected readonly permits = signal<Permit[]>([]);
  protected readonly tasks = signal<PermitTask[]>([]);
  protected readonly tasksLoading = signal(true);
  protected readonly tasksError = signal('');
  protected readonly online = signal(true);
  protected readonly displayName = computed(() => this.identityStore.selected().displayName);
  protected readonly stats = computed(() => {
    const permits = this.permits();
    return [
      {
        label: 'Draft saya',
        value: permits.filter((x) => x.status === 'DRAFT').length,
        tone: 'slate',
        icon: 'document',
      },
      {
        label: 'Menunggu tindakan',
        value: permits.filter((x) =>
          ['UNDER_VALIDATION', 'AWAITING_AREA_APPROVAL', 'CLOSURE_REQUESTED'].includes(x.status),
        ).length,
        tone: 'amber',
        icon: 'clock',
      },
      {
        label: 'Menunggu approval penerbitan',
        value: permits.filter((x) => x.status === 'AWAITING_AREA_APPROVAL').length,
        tone: 'blue',
        icon: 'check',
      },
      {
        label: 'Diterbitkan',
        value: permits.filter((x) => x.status === 'ISSUED').length,
        tone: 'green',
        icon: 'open',
      },
      {
        label: 'Suspended',
        value: permits.filter((x) => x.status === 'SUSPENDED').length,
        tone: 'red',
        icon: 'warning',
      },
    ];
  });
  protected readonly operations = computed(() => {
    const permits = this.permits();
    const now = Date.now();
    const oneDay = 24 * 60 * 60 * 1000;
    return {
      issued: permits.filter((x) => x.status === 'ISSUED').length,
      suspended: permits.filter((x) => x.status === 'SUSPENDED').length,
      expiring: permits.filter((x) => {
        const remaining = new Date(x.draft.validUntil).getTime() - now;
        return (
          remaining >= 0 &&
          remaining <= oneDay &&
          !['CLOSED', 'REJECTED', 'CANCELLED'].includes(x.status)
        );
      }).length,
    };
  });

  constructor() {
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => this.permits.set(response.items),
        error: () => this.online.set(false),
      });

    this.api
      .listTasks()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.tasks.set(response.items);
          this.tasksLoading.set(false);
        },
        error: () => {
          this.tasksError.set('Tugas aktif gagal dimuat. Coba buka halaman Tugas Saya.');
          this.tasksLoading.set(false);
        },
      });
  }

  protected taskAction(type: string): string {
    return (
      {
        HSE_VALIDATION: 'Validasi',
        AREA_APPROVE_AND_ISSUE: 'Setujui & terbitkan',
        AREA_CLOSE_VERIFICATION: 'Verifikasi penutupan',
      }[type] ?? 'Buka'
    );
  }
}
