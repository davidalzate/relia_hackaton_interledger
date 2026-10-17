import {
  ChangeDetectionStrategy,
  Component,
  computed,
  forwardRef,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { InputText } from 'primeng/inputtext';

import {
  COUNTRIES,
  DEFAULT_COUNTRY,
  countryByIso,
  formatLocal,
  splitE164,
  toE164,
} from '../../utils/phone';

/**
 * Celular con selector de país. El valor del control es E.164 ("+573000000001") cuando el número
 * tiene los dígitos del país y '' mientras está incompleto.
 */
@Component({
  selector: 'app-phone-field',
  imports: [InputText],
  templateUrl: './phone-field.component.html',
  styleUrl: './phone-field.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PhoneFieldComponent), multi: true },
  ],
})
export class PhoneFieldComponent implements ControlValueAccessor {
  /** id del input de número: el `<label for>` del padre apunta aquí */
  readonly inputId = input.required<string>();
  readonly describedBy = input<string | null>(null);
  readonly invalid = input(false);
  readonly autocomplete = input('off');

  protected readonly countries = COUNTRIES;
  protected readonly country = signal(DEFAULT_COUNTRY);
  protected readonly local = signal('');
  protected readonly disabled = signal(false);
  protected readonly placeholder = computed(() =>
    formatLocal('0'.repeat(this.country().digits), this.country().digits),
  );

  private onChange: (value: string) => void = () => {};
  protected onTouched: () => void = () => {};

  writeValue(value: string | null): void {
    if (!value) {
      this.local.set('');
      return;
    }
    const { country, local } = splitE164(value);
    this.country.set(country);
    this.local.set(formatLocal(local, country.digits));
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  protected selectCountry(iso: string): void {
    const country = countryByIso(iso);
    // Sin recortar: un número de otro largo queda incompleto en vez de cambiar de dueño
    this.country.set(country);
    this.emit();
  }

  protected typeNumber(input: HTMLInputElement): void {
    const formatted = formatLocal(input.value, this.country().digits);
    // Reescribe el input aunque la señal no cambie (p. ej. al teclear una letra)
    input.value = formatted;
    this.local.set(formatted);
    this.emit();
  }

  private emit(): void {
    this.onChange(toE164(this.country(), this.local()));
  }
}
