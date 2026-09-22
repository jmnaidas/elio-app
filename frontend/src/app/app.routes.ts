import { Routes } from '@angular/router';
import { AppShell } from './core/layout/app-shell';
import { guestGuard, onboardingGuard, workspaceGuard } from './core/auth/auth.guards';
export const routes: Routes = [
  ...['login', 'register', 'forgot-password', 'reset-password', 'verify-email'].map((path) => ({
    path,
    title: `${path === 'login' ? 'Sign in' : path.replaceAll('-', ' ')} · ELIO`,
    data: { mode: path },
    canActivate: path === 'login' || path === 'register' ? [guestGuard] : [],
    loadComponent: () => import('./features/auth/auth-page').then((m) => m.AuthPage),
  })),
  {
    path: 'onboarding',
    title: 'Set up your workspace · ELIO',
    canActivate: [onboardingGuard],
    loadComponent: () =>
      import('./features/onboarding/onboarding-page').then((m) => m.OnboardingPage),
  },
  {
    path: 'session-unavailable',
    title: 'Unable to connect · ELIO',
    loadComponent: () =>
      import('./features/auth/session-unavailable').then((m) => m.SessionUnavailable),
  },
  {
    path: '',
    component: AppShell,
    canActivate: [workspaceGuard],
    canActivateChild: [workspaceGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'Dashboard · ELIO',
        loadComponent: () =>
          import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
      },
      {
        path: 'invoices',
        title: 'Invoices · ELIO',
        loadComponent: () =>
          import('./features/invoices/invoices-page').then((m) => m.InvoicesPage),
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
        loadComponent: () =>
          import('./features/services/services-page').then((m) => m.ServicesPage),
      },
      {
        path: 'settings',
        title: 'Settings · ELIO',
        loadComponent: () =>
          import('./features/settings/settings-page').then((m) => m.SettingsPage),
      },
      {
        path: '**',
        title: 'Page not found · ELIO',
        loadComponent: () => import('./shared/ui/not-found-page').then((m) => m.NotFoundPage),
      },
    ],
  },
];
