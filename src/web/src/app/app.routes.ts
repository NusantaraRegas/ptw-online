import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'permits',
    loadComponent: () => import('./features/permits/permit-list').then((m) => m.PermitList),
  },
  {
    path: 'permits/new',
    loadComponent: () => import('./features/permits/permit-create').then((m) => m.PermitCreate),
  },
  {
    path: 'permits/:id',
    loadComponent: () => import('./features/permits/permit-detail').then((m) => m.PermitDetail),
  },
  {
    path: 'tasks',
    loadComponent: () => import('./features/tasks/task-list').then((m) => m.TaskList),
  },
  {
    path: 'operations',
    loadComponent: () => import('./features/placeholder/placeholder').then((m) => m.OperationsPage),
  },
  {
    path: 'reports',
    loadComponent: () => import('./features/placeholder/placeholder').then((m) => m.ReportsPage),
  },
  {
    path: 'admin/authorizations',
    loadComponent: () =>
      import('./features/admin/admin-authorizations').then((m) => m.AdminAuthorizations),
  },
  {
    path: 'admin/policy',
    loadComponent: () =>
      import('./features/admin/admin-policy-readiness').then((m) => m.AdminPolicyReadiness),
  },
  {
    path: 'admin/policy-uat',
    loadComponent: () => import('./features/admin/admin-policy-uat').then((m) => m.AdminPolicyUat),
  },
  {
    path: 'admin',
    loadComponent: () => import('./features/admin/admin-locations').then((m) => m.AdminLocations),
  },
  { path: '**', redirectTo: '' },
];
