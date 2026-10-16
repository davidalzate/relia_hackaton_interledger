import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { ConfirmationService, MenuItem } from 'primeng/api';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Menu } from 'primeng/menu';

import { AgentService } from '../../core/services/agent.service';
import { AuthService } from '../../core/services/auth.service';
import { DemoPanelComponent } from '../../shared/components/demo-panel/demo-panel.component';
import { ReceiptDialogComponent } from '../../shared/components/receipt-dialog/receipt-dialog.component';

// B0: encabezado fijo y menú del corresponsal; envuelve todas sus vistas.
// También aloja B4 (diálogo de confirmación) para las vistas hijas vía ConfirmationService
@Component({
  selector: 'app-agent-shell',
  imports: [
    RouterLink,
    RouterOutlet,
    Menu,
    ConfirmDialog,
    DemoPanelComponent,
    ReceiptDialogComponent,
  ],
  providers: [ConfirmationService],
  templateUrl: './agent-shell.component.html',
  styleUrl: './agent-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgentShellComponent {
  private readonly auth = inject(AuthService);
  private readonly agent = inject(AgentService);
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

  // Cerrar sesión descarta cualquier flujo en curso (p. ej. un retiro esperando aprobación)
  private logout(): void {
    this.agent.clearWithdrawal();
    this.agent.clearCupoLoad();
    this.auth.clear();
    void this.router.navigateByUrl('/login');
  }
}
