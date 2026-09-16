import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withNavigationErrorHandler } from '@angular/router';
import { developmentIdentityInterceptor } from './core/development-identity';
import { recoverFromStaleChunk } from './core/chunk-load-recovery';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([developmentIdentityInterceptor])),
    provideRouter(
      routes,
      withNavigationErrorHandler((error) => recoverFromStaleChunk(error)),
    ),
  ],
};
