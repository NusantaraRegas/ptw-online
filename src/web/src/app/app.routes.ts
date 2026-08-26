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
    loadComponent: () => import('./features/placeholder/placeholder').then((m) => m.TasksPage),
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
    path: 'admin',
    loadComponent: () => import('./features/placeholder/placeholder').then((m) => m.AdminPage),
  },
  { path: '**', redirectTo: '' },
];
