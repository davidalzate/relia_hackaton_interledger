import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { AdminService } from '../../core/services/admin.service';

// D1: visión global de la red de corresponsales y gestión de comisiones
@Component({
  selector: 'app-admin-home',
  imports: [RouterLink, CurrencyPipe, DatePipe],
  templateUrl: './admin-home.component.html',
  styleUrl: './admin-home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminHomeComponent {
  private readonly admin = inject(AdminService);

  protected readonly agents = this.admin.agents;
  protected readonly totals = this.admin.totals;
  protected readonly payments = this.admin.payments;
  protected readonly loadError = signal(false);

  constructor() {
    // Cada visita a D1 refresca la red (los saldos cambian en vivo desde las tiendas)
    this.admin
      .loadAgents()
      .pipe(takeUntilDestroyed())
      .subscribe({ error: () => this.loadError.set(true) });
    this.admin
      .loadPayments()
      .pipe(takeUntilDestroyed())
      .subscribe({ error: () => undefined });
  }
}
