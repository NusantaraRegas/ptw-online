import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  CurrentIdentity,
  DEVELOPMENT_IDENTITIES,
  DevelopmentIdentityStore,
  IdentityApi,
} from './core/development-identity';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly identityApi = inject(IdentityApi);
  private readonly identityStore = inject(DevelopmentIdentityStore);

  protected readonly menuOpen = signal(false);
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

  constructor() {
    this.identityApi.me().subscribe({
      next: (identity) => this.identity.set(identity),
    });
  }

  protected toggleMenu(): void {
    this.menuOpen.update((value) => !value);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected switchIdentity(key: string): void {
    if (this.identityStore.select(key)) globalThis.location.reload();
  }
}
