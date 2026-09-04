import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  CurrentIdentity,
  DEVELOPMENT_IDENTITIES,
  DevelopmentIdentityStore,
  IdentityApi,
} from './core/development-identity';
import { PermitApi, PermitTask } from './core/permit-api';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly identityApi = inject(IdentityApi);
  private readonly identityStore = inject(DevelopmentIdentityStore);
  private readonly permitApi = inject(PermitApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly menuOpen = signal(false);
  protected readonly notificationsOpen = signal(false);
  protected readonly pendingTaskCount = signal(0);
  protected readonly pendingTasks = signal<PermitTask[]>([]);
  protected readonly identity = signal<CurrentIdentity>({
    ...this.identityStore.selected(),
    isDevelopmentIdentity: false,
  });
  protected readonly developmentProfiles = DEVELOPMENT_IDENTITIES;
  protected readonly selectedIdentityKey = this.identityStore.selectedKey;
  protected readonly initials = computed(() =>
    this.identity()
      .displayName.split(/\s+/)
      .slice(0, 2)
      .map((part) => part.charAt(0))
      .join('')
      .toUpperCase(),
  );
  protected readonly roleLabel = computed(() => this.identity().roles.join(' · '));
  protected readonly isAdministrator = computed(() =>
    this.identity().roles.includes('Administrator'),
  );
  protected readonly taskBadgeText = computed(() =>
    this.pendingTaskCount() > 99 ? '99+' : this.pendingTaskCount().toString(),
  );
  protected readonly taskLinkLabel = computed(() => {
    const count = this.pendingTaskCount();
    return count > 0 ? `Tugas Saya, ${count} tugas perlu perhatian` : 'Tugas Saya';
  });
  protected readonly attentionTasks = computed(() => this.pendingTasks().slice(0, 5));

  constructor() {
    this.identityApi
      .me()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (identity) => this.identity.set(identity),
      });
    this.permitApi
      .listTasks()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.pendingTasks.set(page.items);
          this.pendingTaskCount.set(page.count);
        },
      });
  }

  protected toggleMenu(): void {
    this.menuOpen.update((value) => !value);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected toggleNotifications(event: Event): void {
    event.stopPropagation();
    this.notificationsOpen.update((open) => !open);
  }

  protected keepNotificationsOpen(event: Event): void {
    event.stopPropagation();
  }

  protected closeNotifications(): void {
    this.notificationsOpen.set(false);
  }

  @HostListener('document:click')
  protected closeNotificationsFromOutside(): void {
    this.closeNotifications();
  }

  @HostListener('document:keydown.escape')
  protected closeNotificationsFromKeyboard(): void {
    this.closeNotifications();
  }

  protected switchIdentity(key: string): void {
    if (this.identityStore.select(key)) globalThis.location.reload();
  }
}
