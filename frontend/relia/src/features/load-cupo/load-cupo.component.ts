import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Cargar cupo (solo destino de navegación por ahora)
@Component({
  selector: 'app-load-cupo',
  imports: [RouterLink],
  templateUrl: './load-cupo.component.html',
  styleUrl: './load-cupo.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadCupoComponent {}
