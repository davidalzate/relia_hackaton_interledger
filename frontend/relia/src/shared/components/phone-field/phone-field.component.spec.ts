import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { displayPhone, maskPhone, splitE164 } from '../../utils/phone';
import { PhoneFieldComponent } from './phone-field.component';

@Component({
  imports: [ReactiveFormsModule, PhoneFieldComponent],
  template: `<app-phone-field inputId="phone" [formControl]="phone" />`,
})
class HostComponent {
  readonly phone = new FormControl('', { nonNullable: true });
}

function setup() {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const input = el.querySelector('input') as HTMLInputElement;
  const select = el.querySelector('select') as HTMLSelectElement;
  const type = (value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };
  const pick = (iso: string) => {
    select.value = iso;
    select.dispatchEvent(new Event('change'));
  };
  return { fixture, control: fixture.componentInstance.phone, input, select, type, pick };
}

describe('PhoneFieldComponent', () => {
  it('Colombia por defecto: formatea y emite E.164 solo con el número completo', () => {
    const { control, input, select, type } = setup();
    expect(select.value).toBe('CO');
    type('300-000a0');
    expect(input.value).toBe('300 000 0');
    expect(control.value).toBe('');
    type('300000000199');
    expect(input.value).toBe('300 000 0001');
    expect(control.value).toBe('+573000000001');
  });

  it('cambiar de país usa su indicativo y su largo', () => {
    const { control, type, pick } = setup();
    type('3000000001');
    pick('PE');
    expect(control.value).toBe('');
    type('987654321');
    expect(control.value).toBe('+51987654321');
  });

  it('un valor E.164 del formulario elige el país y llena el número', () => {
    const { fixture, control, input, select } = setup();
    control.setValue('+525512345678');
    fixture.detectChanges();
    expect(select.value).toBe('MX');
    expect(input.value).toBe('551 234 5678');
  });
});

describe('utilidades de celular', () => {
  it('separa, muestra y enmascara E.164', () => {
    expect(splitE164('+593987654321')).toMatchObject({
      country: { iso: 'EC' },
      local: '987654321',
    });
    expect(displayPhone('+573000000001')).toBe('+57 300 000 0001');
    expect(maskPhone('+573000000001')).toBe('+57 300 *** 0001');
  });
});
