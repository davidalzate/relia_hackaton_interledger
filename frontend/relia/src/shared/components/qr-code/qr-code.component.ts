import { ChangeDetectionStrategy, Component, effect, input, signal } from '@angular/core';
import { toString } from 'qrcode';

// QR de un texto (p. ej. la `approvalUrl` de un retiro) como imagen SVG generada en el navegador
@Component({
  selector: 'app-qr-code',
  template: `
    @if (src(); as s) {
      <img [src]="s" [alt]="label()" [width]="size()" [height]="size()" />
    } @else {
      <div class="placeholder" role="img" [attr.aria-label]="label()"></div>
    }
  `,
  styles: `
    :host {
      display: inline-block;
      line-height: 0;
    }
    img,
    .placeholder {
      width: min(100%, 240px);
      height: auto;
      aspect-ratio: 1;
      border-radius: 12px;
      background: #fff;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QrCodeComponent {
  readonly value = input.required<string>();
  readonly label = input('Código QR');
  readonly size = input(240);
  protected readonly src = signal<string | null>(null);

  constructor() {
    effect(() => {
      const value = this.value();
      const width = this.size();
      // Negro sobre blanco aun en modo oscuro: los lectores necesitan contraste
      toString(value, { type: 'svg', width, margin: 1, errorCorrectionLevel: 'M' })
        .then((svg) => {
          if (this.value() === value) {
            this.src.set(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`);
          }
        })
        .catch(() => this.src.set(null));
    });
  }
}
