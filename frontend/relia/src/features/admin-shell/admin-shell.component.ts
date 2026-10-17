import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';
import { ReceiptDialogComponent } from '../../shared/components/receipt-dialog/receipt-dialog.component';

// D0: barra cobalto fija del admin; envuelve todas sus vistas
@Component({
  selector: 'app-admin-shell',
  imports: [RouterLink, RouterOutlet, ReceiptDialogComponent],
  templateUrl: './admin-shell.component.html',
  styleUrl: './admin-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.clear();
    void this.router.navigateByUrl('/login');
  }
}
