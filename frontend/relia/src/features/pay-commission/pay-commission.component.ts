import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Pagar comisión (solo destino de navegación por ahora)
@Component({
  selector: 'app-pay-commission',
  imports: [RouterLink],
  templateUrl: './pay-commission.component.html',
  styleUrl: './pay-commission.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PayCommissionComponent {}
