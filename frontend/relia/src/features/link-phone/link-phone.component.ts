import { Location } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { InputText } from 'primeng/inputtext';

import { ApiError } from '../../core/http/api';
import { KeyLinkService, PendingLink } from '../../core/services/key-link.service';
import { ErrorCode } from '../../shared/schemas/problem.schema';
import { PhoneFieldComponent } from '../../shared/components/phone-field/phone-field.component';
import { displayPhone } from '../../shared/utils/phone';

/** `WalletAddress` del contrato: `$dominio/ruta` */
const POINTER = /^\$[^\s/]+\/\S+$/;

const LINK_ERROR: Partial<Record<ErrorCode, string>> = {
  key_already_linked: 'Este celular ya tiene una billetera vinculada.',
  wallet_already_linked: 'Esta billetera ya está vinculada a otro celular.',
  phone_reserved: 'Este celular o esta billetera pertenecen a una tienda o a Relia.',
  wallet_not_found: 'No encontramos ese payment pointer en la red de prueba.',
  rate_limited: 'Demasiados intentos. Espera un momento y vuelve a intentar.',
  rail_unavailable: 'La red de pagos no responde. Intenta en un momento.',
};

type Step = 'form' | 'code' | 'done';

const foreignCurrency = (link: PendingLink | null): string | null =>
  link && link.walletAssetCode !== 'COP' ? link.walletAssetCode : null;

// C1: página pública para que el usuario asocie su celular (llave) a su payment pointer
@Component({
  selector: 'app-link-phone',
  imports: [ReactiveFormsModule, RouterLink, InputText, PhoneFieldComponent],
  templateUrl: './link-phone.component.html',
  styleUrl: './link-phone.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LinkPhoneComponent {
  private readonly keyLinks = inject(KeyLinkService);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);

  private readonly pending = this.keyLinks.pending();
  protected readonly step = signal<Step>(this.pending ? 'code' : 'form');
  protected readonly submitted = signal(false);
  protected readonly codeError = signal<string | null>(null);
  protected readonly linkError = signal<string | null>(null);
  protected readonly linking = signal(false);

  protected readonly form = this.fb.group({
    // El enlace del WhatsApp de invitación trae ?phone= en E.164
    phone: [
      this.pending?.phone ?? inject(ActivatedRoute).snapshot.queryParamMap.get('phone') ?? '',
    ],
    walletAddress: [this.pending?.walletAddress ?? ''],
    code: [''],
  });
  private readonly value = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  /** E.164, o '' mientras el número está incompleto */
  protected readonly phone = computed(() => this.value().phone ?? '');
  protected readonly formattedPhone = computed(() => displayPhone(this.phone()));
  protected readonly walletAddress = computed(() => (this.value().walletAddress ?? '').trim());
  protected readonly phoneInvalid = computed(() => !this.phone());
  protected readonly pointerInvalid = computed(() => !POINTER.test(this.walletAddress()));
  /** Vinculación recién verificada: el servicio limpia `pending()` al verificar y la pantalla final la sigue mostrando */
  private readonly linked = signal<PendingLink | null>(null);
  /** "Billetera en USD": solo si la billetera no es COP (`walletAssetCode` de `POST /key-links`) */
  protected readonly walletCurrency = computed(() =>
    foreignCurrency(this.keyLinks.pending() ?? this.linked()),
  );

  /** C1.1 → C1.2. También "Reenviar código": el API crea el desafío y manda el OTP */
  protected sendCode(): void {
    this.submitted.set(true);
    if (this.phoneInvalid() || this.pointerInvalid() || this.linking()) return;
    this.codeError.set(null);
    this.linkError.set(null);
    this.linking.set(true);
    this.keyLinks
      .start(this.phone(), this.walletAddress())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.linking.set(false);
          this.form.controls.code.reset();
          this.step.set('code');
        },
        error: (err: unknown) => this.fail(err),
      });
  }

  /** C1.2: el API verifica el OTP y crea la llave */
  protected verify(): void {
    if (this.linking()) return;
    this.codeError.set(null);
    this.linkError.set(null);
    this.linking.set(true);
    const link = this.keyLinks.pending();
    this.keyLinks
      .verify(this.form.controls.code.value.trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.linking.set(false);
          this.linked.set(link);
          this.step.set('done');
        },
        error: (err: unknown) => this.fail(err),
      });
  }

  private fail(err: unknown): void {
    this.linking.set(false);
    const problem = err instanceof ApiError ? err.problem : null;
    switch (problem?.code) {
      case 'invalid_otp':
        this.codeError.set(
          `El código no coincide. Revisa el SMS. Te quedan ${problem.attemptsRemaining ?? 0} intentos.`,
        );
        return;
      case 'otp_expired':
      case 'not_found':
        this.codeError.set('El código venció. Pide uno nuevo.');
        return;
      case 'otp_attempts_exceeded':
        this.codeError.set('Superaste los intentos. Pide un código nuevo.');
        return;
    }
    this.linkError.set(
      (problem && LINK_ERROR[problem.code]) ?? 'No pudimos vincular. Intenta de nuevo.',
    );
  }

  protected changeData(): void {
    this.keyLinks.reset();
    this.linked.set(null);
    this.step.set('form');
    this.linkError.set(null);
  }

  /** Vuelve a la pantalla anterior (depósito o retiro en curso), donde se valida la llave otra vez */
  protected back(): void {
    this.location.back();
  }
}
