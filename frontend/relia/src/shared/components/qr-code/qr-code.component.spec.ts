import { TestBed } from '@angular/core/testing';

import { QrCodeComponent } from './qr-code.component';

describe('QrCodeComponent', () => {
  it('genera un SVG con el texto y su etiqueta accesible', async () => {
    const fixture = TestBed.createComponent(QrCodeComponent);
    fixture.componentRef.setInput('value', 'https://wallet.test/aprobar/123');
    fixture.componentRef.setInput('label', 'QR para aprobar el pago');
    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('img')).toBeTruthy();
    });
    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    expect(img.getAttribute('src')).toMatch(/^data:image\/svg\+xml/);
    expect(img.alt).toBe('QR para aprobar el pago');
  });
});
