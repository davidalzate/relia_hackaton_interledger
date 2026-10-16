import { ChangeDetectionStrategy, Component } from '@angular/core';

// Pendiente: pantalla Inicio admin (solo destino de navegación por ahora)
@Component({
  selector: 'app-admin-home',
  templateUrl: './admin-home.component.html',
  styleUrl: './admin-home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminHomeComponent {}
