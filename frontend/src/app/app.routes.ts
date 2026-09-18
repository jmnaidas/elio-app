import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Dashboard · ELIO',
    loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
  },
  {
    path: 'invoices',
    title: 'Invoices · ELIO',
    loadComponent: () => import('./features/invoices/invoices-page').then((m) => m.InvoicesPage),
  },
  {
    path: 'receivables',
    title: 'Receivables · ELIO',
    loadComponent: () =>
      import('./features/receivables/receivables-page').then((m) => m.ReceivablesPage),
  },
  {
    path: 'clients',
    title: 'Clients · ELIO',
    loadComponent: () => import('./features/clients/clients-page').then((m) => m.ClientsPage),
  },
  {
    path: 'services',
    title: 'Services · ELIO',
    loadComponent: () => import('./features/services/services-page').then((m) => m.ServicesPage),
  },
  {
    path: 'settings',
    title: 'Settings · ELIO',
    loadComponent: () => import('./features/settings/settings-page').then((m) => m.SettingsPage),
  },
  {
    path: '**',
    title: 'Page not found · ELIO',
    loadComponent: () => import('./shared/ui/not-found-page').then((m) => m.NotFoundPage),
  },
];
