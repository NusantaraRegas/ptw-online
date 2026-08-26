import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Permit, PermitApi } from '../../core/permit-api';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, DatePipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(PermitApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly permits = signal<Permit[]>([]);
  protected readonly online = signal(true);
  protected readonly stats = computed(() => {
    const permits = this.permits();
    return [
      {
        label: 'Draft saya',
        value: permits.filter((x) => x.status === 'DRAFT').length,
        tone: 'slate',
        icon: 'document',
      },
      { label: 'Menunggu tindakan', value: 4, tone: 'amber', icon: 'clock' },
      {
        label: 'Disetujui, belum open',
        value: permits.filter((x) =>
          ['APPROVED', 'READYFORISSUE', 'READY_FOR_ISSUE'].includes(x.status),
        ).length,
        tone: 'blue',
        icon: 'check',
      },
      {
        label: 'Open',
        value: permits.filter((x) => x.status === 'OPEN').length,
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

  constructor() {
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => this.permits.set(response.items),
        error: () => this.online.set(false),
      });
  }
}
