import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { Menu } from 'primeng/menu';

import { AuthService } from '../../core/services/auth.service';

// B0: encabezado fijo y menú del corresponsal; envuelve todas sus vistas
@Component({
  selector: 'app-agent-shell',
  imports: [RouterLink, RouterOutlet, Menu],
  templateUrl: './agent-shell.component.html',
  styleUrl: './agent-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgentShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly user = this.auth.user;
  protected readonly store = computed(() => this.user()?.store ?? '');
  protected readonly menuOpen = signal(false);

  protected readonly items: MenuItem[] = [
    { label: 'Historial', routerLink: '/caja/historial' },
    { label: 'Efectivo en caja', routerLink: '/caja/efectivo' },
    { separator: true },
    { label: 'Cerrar sesión', styleClass: 'menu-danger', command: () => this.logout() },
  ];

  // Limpiar la sesión descarta cualquier flujo en curso (todo vive en memoria)
  private logout(): void {
    this.auth.clear();
    void this.router.navigateByUrl('/login');
  }
}
