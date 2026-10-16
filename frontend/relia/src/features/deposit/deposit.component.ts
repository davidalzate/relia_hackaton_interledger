import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Depositar (solo destino de navegación por ahora)
@Component({
  selector: 'app-deposit',
  imports: [RouterLink],
  templateUrl: './deposit.component.html',
  styleUrl: './deposit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DepositComponent {}
