import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { PermitWorkflow } from '../../core/permit-api';

@Component({
  selector: 'app-permit-validation-progress',
  imports: [DatePipe],
  templateUrl: './permit-validation-progress.html',
  styleUrl: './permit-validation-progress.scss',
})
export class PermitValidationProgress {
  readonly workflow = input.required<PermitWorkflow>();

  protected readonly completedCount = computed(() => Number(this.workflow().hsse.completed));
  protected readonly approvalState = computed(() => {
    if (this.workflow().approvedBy) return 'Disetujui';
    return this.completedCount() === 1 ? 'Menunggu approval' : 'Menunggu validasi';
  });
}
