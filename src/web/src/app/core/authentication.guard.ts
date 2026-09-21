import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { clearClientAuthMode, hasClientAuthMode, IdentityApi } from './development-identity';

export const authenticatedGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const login = () =>
    router.createUrlTree(['/login'], {
      queryParams: state.url === '/' ? undefined : { returnUrl: state.url },
    });

  if (!hasClientAuthMode()) return login();

  return inject(IdentityApi)
    .me()
    .pipe(
      map(() => true),
      catchError(() => {
        clearClientAuthMode();
        return of(login());
      }),
    );
};
