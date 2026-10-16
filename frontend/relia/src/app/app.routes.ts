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
    canActivate: [authGuard],
    data: { role: 'agent' },
    loadComponent: () =>
      import('../features/agent-shell/agent-shell.component').then((m) => m.AgentShellComponent),
    children: [
      {
        path: '',
        title: 'Caja | Relia',
        loadComponent: () => import('../features/home/home.component').then((m) => m.HomeComponent),
      },
      {
        path: 'depositar',
        title: 'Depositar | Relia',
        loadComponent: () =>
          import('../features/deposit/deposit.component').then((m) => m.DepositComponent),
      },
      {
        path: 'retirar',
        title: 'Retirar | Relia',
        loadComponent: () =>
          import('../features/withdraw/withdraw.component').then((m) => m.WithdrawComponent),
      },
      {
        path: 'cupo',
        title: 'Cargar cupo | Relia',
        loadComponent: () =>
          import('../features/load-cupo/load-cupo.component').then((m) => m.LoadCupoComponent),
      },
      {
        path: 'historial',
        title: 'Historial | Relia',
        loadComponent: () =>
          import('../features/history/history.component').then((m) => m.HistoryComponent),
      },
      {
        path: 'efectivo',
        title: 'Efectivo en caja | Relia',
        loadComponent: () =>
          import('../features/cash-on-hand/cash-on-hand.component').then(
            (m) => m.CashOnHandComponent,
          ),
      },
    ],
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
