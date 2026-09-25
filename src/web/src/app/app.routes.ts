import { Routes } from '@angular/router';
import { authenticatedGuard } from './core/authentication.guard';
import { operationsBoardGuard } from './core/operations-board.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'permits',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/permits/permit-list').then((m) => m.PermitList),
  },
  {
    path: 'permits/new',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/permits/permit-create').then((m) => m.PermitCreate),
  },
  {
    path: 'permits/:id',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/permits/permit-detail').then((m) => m.PermitDetail),
  },
  {
    path: 'tasks',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/tasks/task-list').then((m) => m.TaskList),
  },
  {
    path: 'operations',
    canActivate: [authenticatedGuard, operationsBoardGuard],
    loadComponent: () =>
      import('./features/operations/operations-board').then((m) => m.OperationsBoard),
  },
  {
    path: 'reports',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/placeholder/placeholder').then((m) => m.ReportsPage),
  },
  {
    path: 'admin/users',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/admin/admin-users').then((m) => m.AdminUsers),
  },
  {
    path: 'admin/authorizations',
    canActivate: [authenticatedGuard],
    loadComponent: () =>
      import('./features/admin/admin-authorizations').then((m) => m.AdminAuthorizations),
  },
  {
    path: 'admin/policy',
    canActivate: [authenticatedGuard],
    loadComponent: () =>
      import('./features/admin/admin-policy-readiness').then((m) => m.AdminPolicyReadiness),
  },
  {
    path: 'admin/policy-uat',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/admin/admin-policy-uat').then((m) => m.AdminPolicyUat),
  },
  {
    path: 'admin/settings',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/admin/admin-settings').then((m) => m.AdminSettings),
  },
  {
    path: 'admin',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./features/admin/admin-locations').then((m) => m.AdminLocations),
  },
  { path: '**', redirectTo: '' },
];
