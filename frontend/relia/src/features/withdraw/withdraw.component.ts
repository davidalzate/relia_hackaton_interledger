import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Retirar (solo destino de navegación por ahora)
@Component({
  selector: 'app-withdraw',
  imports: [RouterLink],
  templateUrl: './withdraw.component.html',
  styleUrl: './withdraw.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WithdrawComponent {}
