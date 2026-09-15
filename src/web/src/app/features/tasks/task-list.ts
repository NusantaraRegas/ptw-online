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
        HSE_VALIDATION: 'HSE',
        AREA_APPROVE_AND_ISSUE: 'TERBIT',
        AREA_CLOSE_VERIFICATION: 'TUTUP',
      }[type] ?? 'PTW'
    );
  }
}
