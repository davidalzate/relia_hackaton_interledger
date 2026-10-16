import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
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
}
