import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Pendiente: pantalla Detalle del corresponsal (solo destino de navegación por ahora)
@Component({
  selector: 'app-agent-detail',
  imports: [RouterLink],
  templateUrl: './agent-detail.component.html',
  styleUrl: './agent-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgentDetailComponent {}
