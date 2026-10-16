import { ChangeDetectionStrategy, Component } from '@angular/core';

// Pendiente: pantalla Vincula tu celular (solo destino de navegación por ahora)
@Component({
  selector: 'app-link-phone',
  templateUrl: './link-phone.component.html',
  styleUrl: './link-phone.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LinkPhoneComponent {}
