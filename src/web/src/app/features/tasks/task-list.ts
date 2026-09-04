import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { PermitApi, PermitTask } from '../../core/permit-api';

@Component({
  selector: 'app-task-list',
  imports: [DatePipe, RouterLink],
  templateUrl: './task-list.html',
  styleUrl: './task-list.scss',
})
export class TaskList {
  private readonly api = inject(PermitApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly tasks = signal<PermitTask[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  constructor() {
    this.api
      .listTasks()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.tasks.set(page.items);
          this.loading.set(false);
        },
        error: (response) => {
          this.error.set(response?.error?.detail ?? 'Tugas gagal dimuat. Coba lagi.');
          this.loading.set(false);
        },
      });
  }

  protected taskCode(type: string): string {
    return (
      {
        HSSE_VALIDATION: 'HSSE',
        AREA_OWNER_APPROVAL: 'APR',
        AREA_OWNER_ISSUE: 'TERBIT',
        SUSPENSION_APPROVAL: 'TUNDA',
        HSSE_COMPLETION_CONFIRMATION: 'HSSE',
        AREA_OWNER_COMPLETION_CONFIRMATION: 'SELESAI',
        AREA_OWNER_CLOSE: 'TUTUP',
      }[type] ?? 'PTW'
    );
  }
}
