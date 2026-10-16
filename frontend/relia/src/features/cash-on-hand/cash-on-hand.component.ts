import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Efectivo en caja (solo destino de navegación por ahora)
@Component({
  selector: 'app-cash-on-hand',
  imports: [RouterLink],
  templateUrl: './cash-on-hand.component.html',
  styleUrl: './cash-on-hand.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CashOnHandComponent {}
