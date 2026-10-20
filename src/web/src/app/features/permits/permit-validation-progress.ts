import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { PermitInitiation, PermitWorkflow } from '../../core/permit-api';

const INITIATION_STATUS_LABELS: Readonly<Record<string, string>> = {
  DRAFTING: 'Sedang diisi',
  AWAITING_SPONSOR_APPROVAL: 'Menunggu',
  RETURNED: 'Dikembalikan',
  APPROVED: 'Selesai',
  REJECTED: 'Ditolak',
};

@Component({
  selector: 'app-permit-validation-progress',
  imports: [DatePipe],
  templateUrl: './permit-validation-progress.html',
  styleUrl: './permit-validation-progress.scss',
})
export class PermitValidationProgress {
  readonly workflow = input.required<PermitWorkflow>();
  /** Sponsor profile name from the permit; the initiation payload carries no Sponsor name. */
  readonly sponsorName = input<string | null>(null);

  /** Contractor-initiated PTWs add the Sponsor's initial approval as the first stage. */
  protected readonly initiation = computed<PermitInitiation | null>(() => {
    const initiation = this.workflow().initiation;
    return initiation?.initiated ? initiation : null;
  });
  protected readonly initiationCompleted = computed(() => this.initiation()?.status === 'APPROVED');
  protected readonly initiationState = computed(
    () => INITIATION_STATUS_LABELS[this.initiation()?.status ?? ''] ?? 'Menunggu',
  );
  protected readonly stepOffset = computed(() => (this.initiation() ? 1 : 0));
  protected readonly totalCount = computed(() => 3 + this.stepOffset());
  protected readonly completedCount = computed(
    () =>
      Number(this.initiationCompleted()) +
      Number(this.workflow().hse.completed) +
      Number(this.workflow().areaOperations.completed) +
      Number(this.workflow().approval.completed),
  );
  protected readonly approvalState = computed(() => {
    if (this.workflow().approval.completed) return 'Diterbitkan';
    return this.workflow().areaOperations.completed ? 'Menunggu approval' : 'Belum tersedia';
  });
}
