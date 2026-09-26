import { Component, computed, DestroyRef, HostListener, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { catchError, EMPTY, filter, interval, switchMap } from 'rxjs';
import {
  canAccessOperationsBoard,
  clearClientAuthMode,
  CurrentIdentity,
  DEVELOPMENT_IDENTITIES,
  DevelopmentIdentityStore,
  hasClientAuthMode,
  IdentityApi,
  roleDisplayLabel,
} from './core/development-identity';
import { PermitApi, PermitTask } from './core/permit-api';
import { ApplicationSettingsApi, UserGuideSetting } from './core/application-settings-api';

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
  private readonly router = inject(Router);
  private readonly applicationSettingsApi = inject(ApplicationSettingsApi);

  protected readonly menuOpen = signal(false);
  protected readonly notificationsOpen = signal(false);
  protected readonly loginPage = signal(
    !hasClientAuthMode() || globalThis.location.pathname.endsWith('/login'),
  );
  protected readonly pendingTaskCount = signal(0);
  protected readonly pendingTasks = signal<PermitTask[]>([]);
  protected readonly userGuide = signal<UserGuideSetting | null>(null);
  protected readonly userGuideAvailable = computed(() => this.userGuide()?.available === true);
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
  protected readonly roleLabel = computed(() => roleDisplayLabel(this.identity().roles));
  protected readonly isAdministrator = computed(() =>
    this.identity().roles.includes('Administrator'),
  );
  protected readonly canOpenOperationsBoard = computed(() =>
    canAccessOperationsBoard(this.identity().roles),
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
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((event) => this.loginPage.set(event.urlAfterRedirects.startsWith('/login')));

    if (!hasClientAuthMode()) return;

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
    // The guide menu only appears once an Administrator has published a PDF; the shell reads
    // the metadata once per load and never polls it.
    this.applicationSettingsApi
      .userGuide()
      .pipe(
        catchError(() => EMPTY),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((guide) => this.userGuide.set(guide));
    interval(30_000)
      .pipe(
        switchMap(() => this.permitApi.listTasks().pipe(catchError(() => EMPTY))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.pendingTasks.set(page.items);
        this.pendingTaskCount.set(page.count);
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

  protected logout(): void {
    this.identityApi.logout().subscribe({
      next: () => {
        clearClientAuthMode();
        globalThis.location.assign('/login');
      },
    });
  }

  protected downloadUserGuide(): void {
    const guide = this.userGuide();
    if (!guide?.available) return;
    this.applicationSettingsApi
      .downloadUserGuide()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = guide.fileName ?? 'panduan-pengguna.pdf';
        link.click();
        URL.revokeObjectURL(url);
        this.closeMenu();
      });
  }
}
