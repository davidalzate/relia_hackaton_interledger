import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla B9 Comprobante (solo destino de navegación por ahora)
@Component({
  selector: 'app-receipt',
  imports: [RouterLink],
  templateUrl: './receipt.component.html',
  styleUrl: './receipt.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptComponent {
  /** Parámetro de ruta :id (withComponentInputBinding) */
  readonly id = input.required<string>();
}
