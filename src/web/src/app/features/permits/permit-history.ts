import { DatePipe } from '@angular/common';
import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PermitActivity, PermitApi, PermitVersion } from '../../core/permit-api';

@Component({
  selector: 'app-permit-history',
  imports: [DatePipe],
  templateUrl: './permit-history.html',
  styleUrl: './permit-history.scss',
})
export class PermitHistory {
  private readonly api = inject(PermitApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly pageSize = 10;

  readonly permitId = input.required<string>();
  readonly revision = input.required<number>();

  protected readonly tab = signal<'activity' | 'versions'>('activity');
  protected readonly activity = signal<PermitActivity[]>([]);
  protected readonly versions = signal<PermitVersion[]>([]);
  protected readonly activityCount = signal(0);
  protected readonly versionCount = signal(0);
  protected readonly activityLoading = signal(false);
  protected readonly versionsLoading = signal(false);
  protected readonly activityError = signal('');
  protected readonly versionsError = signal('');

  constructor() {
    effect(() => {
      this.permitId();
      this.revision();
      this.activity.set([]);
      this.versions.set([]);
      this.loadActivity();
      this.loadVersions();
    });
  }

  protected selectTab(tab: 'activity' | 'versions'): void {
    this.tab.set(tab);
  }

  protected loadMoreActivity(): void {
    this.loadActivity(this.activity().length);
  }

  protected loadMoreVersions(): void {
    this.loadVersions(this.versions().length);
  }

  protected eventLabel(eventType: string): string {
    const labels: Record<string, string> = {
      permit_draft_created: 'Draft PTW dibuat',
      permit_draft_updated: 'Draft PTW diperbarui',
      permit_submitted: 'PTW diajukan',
      review_started: 'Review dimulai',
      revision_requested: 'Revisi diminta',
      reviews_endorsed: 'Seluruh review disahkan',
      permit_approved: 'PTW disetujui',
      readiness_completed: 'Prasyarat penerbitan selesai',
      work_period_opened: 'Periode kerja dibuka',
      work_period_closed: 'Periode kerja ditutup',
      permit_suspended: 'PTW ditangguhkan',
      permit_resumed: 'Penangguhan diselesaikan',
      work_completed: 'Pekerjaan dinyatakan selesai',
      permit_closed: 'PTW ditutup',
      permit_rejected: 'PTW ditolak',
      permit_cancelled: 'PTW dibatalkan',
      permit_expired: 'Masa berlaku PTW berakhir',
    };
    return labels[eventType] ?? eventType.replaceAll('_', ' ');
  }

  protected actorInitials(actorId: string): string {
    return actorId
      .split(/[.\s_-]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join('');
  }

  protected shortHash(hash: string): string {
    return hash.slice(0, 12);
  }

  private loadActivity(offset = 0): void {
    this.activityLoading.set(true);
    this.activityError.set('');
    this.api
      .listActivity(this.permitId(), offset, this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.activity.update((items) => (offset === 0 ? page.items : [...items, ...page.items]));
          this.activityCount.set(page.count);
          this.activityLoading.set(false);
        },
        error: (response) => {
          this.activityLoading.set(false);
          this.activityError.set(response?.error?.detail ?? 'Riwayat aktivitas gagal dimuat.');
        },
      });
  }

  private loadVersions(offset = 0): void {
    this.versionsLoading.set(true);
    this.versionsError.set('');
    this.api
      .listVersions(this.permitId(), offset, this.pageSize)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.versions.update((items) => (offset === 0 ? page.items : [...items, ...page.items]));
          this.versionCount.set(page.count);
          this.versionsLoading.set(false);
        },
        error: (response) => {
          this.versionsLoading.set(false);
          this.versionsError.set(response?.error?.detail ?? 'Riwayat versi gagal dimuat.');
        },
      });
  }
}
