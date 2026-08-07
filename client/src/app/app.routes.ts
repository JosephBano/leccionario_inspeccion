import type { Routes } from '@angular/router';

import { authGuard, noAuthGuard } from '@core/auth/guards/auth.guard';
import { roleGuard } from '@core/auth/guards/role.guard';
import { Roles } from '@core/auth/models/roles';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [noAuthGuard],
    loadComponent: () =>
      import('@layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('@features/auth/pages/login/login.page').then((m) => m.LoginPage),
      },
    ],
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('@layout/main-layout/main-layout').then((m) => m.MainLayout),
    children: [
      {
        path: '',
        pathMatch: 'full',
        redirectTo: 'mis-paralelos',
      },
      {
        path: 'mis-paralelos',
        canActivate: [roleGuard],
        data: { roles: [Roles.docente] },
        loadComponent: () =>
          import('@features/docente/pages/mis-paralelos/mis-paralelos.page').then(
            (m) => m.MisParalelosPage,
          ),
      },
      {
        path: 'paralelos/:idAsignacion/pasar-lista',
        canActivate: [roleGuard],
        data: { roles: [Roles.docente] },
        loadComponent: () =>
          import('@features/docente/pages/pasar-lista/pasar-lista.page').then(
            (m) => m.PasarListaPage,
          ),
      },
      {
        path: 'reportes',
        canActivate: [roleGuard],
        data: { roles: [Roles.inspector] },
        loadComponent: () =>
          import('@features/inspector/pages/reportes/reportes.page').then(
            (m) => m.ReportesPage,
          ),
      },
      {
        path: 'unauthorized',
        loadComponent: () =>
          import('@features/shared-pages/unauthorized/unauthorized.page').then(
            (m) => m.UnauthorizedPage,
          ),
      },
      {
        path: 'en-construccion',
        loadComponent: () =>
          import('@features/shared-pages/under-construction/under-construction.page').then(
            (m) => m.UnderConstructionPage,
          ),
      },
    ],
  },
  {
    path: '**',
    loadComponent: () =>
      import('@features/shared-pages/not-found/not-found.page').then((m) => m.NotFoundPage),
  },
];
