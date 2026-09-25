import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Permit, PermitApi, PermitTask } from '../../core/permit-api';
import {
  canAccessOperationsBoard,
  canMonitorAllPermits,
  CurrentIdentity,
  IdentityApi,
} from '../../core/development-identity';
import { OperationsApi, OperationsBoardResponse } from '../../core/operations-api';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(PermitApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly identityApi = inject(IdentityApi);
  private readonly operationsApi = inject(OperationsApi);
  protected readonly permits = signal<Permit[]>([]);
  protected readonly tasks = signal<PermitTask[]>([]);
  protected readonly tasksLoading = signal(true);
  protected readonly tasksError = signal('');
  protected readonly online = signal(true);
  protected readonly identity = signal<CurrentIdentity | null>(null);
  protected readonly administratorBoard = signal<OperationsBoardResponse | null>(null);
  protected readonly isAdministrator = computed(() =>
    (this.identity()?.roles ?? []).includes('Administrator'),
  );
  protected readonly isGlobalMonitor = computed(() => {
    const identity = this.identity();
    return (
      this.isAdministrator() ||
      canMonitorAllPermits(identity?.roles ?? [], identity?.locationScopes ?? [])
    );
  });
  protected readonly monitoredPermits = computed(() =>
    this.isGlobalMonitor()
      ? this.permits().filter((permit) => permit.status !== 'DRAFT')
      : this.permits(),
  );
  protected readonly displayName = computed(() => this.identity()?.displayName ?? 'Pengguna PTW');
  protected readonly canCreatePermit = computed(() =>
    (this.identity()?.roles ?? []).some((role) => ['Sponsor', 'Administrator'].includes(role)),
  );
  protected readonly canOpenOperationsBoard = computed(() =>
    canAccessOperationsBoard(this.identity()?.roles ?? []),
  );
  protected readonly currentDateLabel = new Intl.DateTimeFormat('id-ID', {
    weekday: 'long',
    day: '2-digit',
    month: 'long',
    year: 'numeric',
    timeZone: 'Asia/Jakarta',
  }).format(new Date());
  protected readonly stats = computed(() => {
    const permits = this.monitoredPermits();
    if (this.isGlobalMonitor()) {
      const metrics = this.administratorBoard()?.metrics;
      return [
        {
          label: 'Total dipantau',
          value: metrics?.total ?? permits.length,
          tone: 'slate',
          icon: 'document',
        },
        {
          label: 'Dalam proses',
          value:
            metrics == null
              ? permits.filter((x) =>
                  ['UNDER_VALIDATION', 'REVISION_REQUIRED', 'AWAITING_AREA_APPROVAL'].includes(
                    x.status,
                  ),
                ).length
              : metrics.underValidation + metrics.revisionRequired + metrics.awaitingAreaApproval,
          tone: 'amber',
          icon: 'clock',
        },
        {
          label: 'Diterbitkan',
          value: metrics?.issued ?? permits.filter((x) => x.status === 'ISSUED').length,
          tone: 'green',
          icon: 'open',
        },
        {
          label: 'Ditangguhkan',
          value: metrics?.suspended ?? permits.filter((x) => x.status === 'SUSPENDED').length,
          tone: 'red',
          icon: 'warning',
        },
        {
          label: 'Ditutup',
          value: metrics?.closed ?? permits.filter((x) => x.status === 'CLOSED').length,
          tone: 'blue',
          icon: 'check',
        },
      ];
    }
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
        label: 'Ditangguhkan',
        value: permits.filter((x) => x.status === 'SUSPENDED').length,
        tone: 'red',
        icon: 'warning',
      },
    ];
  });
  protected readonly operations = computed(() => {
    const administratorMetrics = this.isGlobalMonitor() ? this.administratorBoard()?.metrics : null;
    if (administratorMetrics) {
      return {
        issued: administratorMetrics.issued,
        suspended: administratorMetrics.suspended,
        expiring: administratorMetrics.expiringSoon,
      };
    }
    const permits = this.monitoredPermits();
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
    this.identityApi
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (identity) => {
          this.identity.set(identity);
          if (
            identity.roles.includes('Administrator') ||
            canMonitorAllPermits(identity.roles, identity.locationScopes)
          ) {
            this.operationsApi
              .list({ offset: 0, limit: 4 })
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                next: (response) => this.administratorBoard.set(response),
                error: () => this.online.set(false),
              });
          }
        },
      });

    this.api
      .list({ offset: 0, limit: 25 })
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
        AREA_OPERATION_REVIEW: 'Verifikasi Bagian 7',
        AREA_APPROVE_AND_ISSUE: 'Setujui & terbitkan',
        AREA_CLOSE_VERIFICATION: 'Verifikasi penutupan',
      }[type] ?? 'Buka'
    );
  }

  protected statusLabel(status: string): string {
    return (
      {
        DRAFT: 'Draft',
        REVISION_REQUIRED: 'Perlu revisi',
        UNDER_VALIDATION: 'Validasi HSE berjalan',
        AWAITING_AREA_APPROVAL: 'Menunggu persetujuan',
        ISSUED: 'Diterbitkan',
        SUSPENDED: 'Ditangguhkan',
        CLOSURE_REQUESTED: 'Menunggu penutupan',
        CLOSED: 'Ditutup',
        REJECTED: 'Ditolak',
        CANCELLED: 'Dibatalkan',
        EXPIRED: 'Kedaluwarsa',
      }[status] ?? status
    );
  }
}
