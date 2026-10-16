import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Historial (solo destino de navegación por ahora)
@Component({
  selector: 'app-history',
  imports: [RouterLink],
  templateUrl: './history.component.html',
  styleUrl: './history.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HistoryComponent {}
