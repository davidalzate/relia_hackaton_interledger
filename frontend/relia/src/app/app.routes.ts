import { Routes } from '@angular/router';
import { authGuard } from '../core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  {
    path: 'login',
    title: 'Entrar | Relia',
    loadComponent: () => import('../features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'caja',
    title: 'Caja | Relia',
    canActivate: [authGuard],
    data: { role: 'agent' },
    loadComponent: () => import('../features/home/home.component').then((m) => m.HomeComponent),
  },
  {
    path: 'admin',
    title: 'Inicio | Relia Admin',
    canActivate: [authGuard],
    data: { role: 'admin' },
    loadComponent: () =>
      import('../features/admin-home/admin-home.component').then((m) => m.AdminHomeComponent),
  },
  {
    path: 'vincular',
    title: 'Vincula tu celular | Relia',
    loadComponent: () =>
      import('../features/link-phone/link-phone.component').then((m) => m.LinkPhoneComponent),
  },
  {
    path: '**',
    title: 'Página no encontrada | Relia',
    loadComponent: () =>
      import('../features/not-found/not-found.component').then((m) => m.NotFoundComponent),
  },
];
