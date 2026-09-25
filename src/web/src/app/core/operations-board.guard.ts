import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { canAccessOperationsBoard, IdentityApi } from './development-identity';

export const operationsBoardGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(IdentityApi)
    .me()
    .pipe(
      map((identity) =>
        canAccessOperationsBoard(identity.roles) ? true : router.createUrlTree(['/']),
      ),
      catchError(() => of(router.createUrlTree(['/login']))),
    );
};
