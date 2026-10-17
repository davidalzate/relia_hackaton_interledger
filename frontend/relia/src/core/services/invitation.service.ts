import { Injectable, signal } from '@angular/core';

export const INVITE_MESSAGE = 'Hola, te invito a vincular tu wallet en relia.com/selectkey';

export interface Invitation {
  /** E.164 */
  phone: string;
  link: string;
  message: string;
  sentAt: string;
}

/** Enlace de WhatsApp con el mensaje de invitación; wa.me usa el E.164 sin "+" */
export const whatsappLink = (phone: string): string =>
  `https://wa.me/${phone.replace('+', '')}?text=${encodeURIComponent(INVITE_MESSAGE)}`;

// Simulado a propósito para el demo (sin POST /keys/{phone}/invitations ni WhatsApp real): guarda
// la invitación para mostrarla en el teléfono simulado (C2), que abre C1 con el celular precargado
@Injectable({ providedIn: 'root' })
export class InvitationService {
  /** Más recientes primero */
  private readonly _invitations = signal<Invitation[]>([]);
  readonly invitations = this._invitations.asReadonly();

  send(phone: string): Invitation {
    const invitation: Invitation = {
      phone,
      link: whatsappLink(phone),
      message: INVITE_MESSAGE,
      sentAt: new Date().toISOString(),
    };
    this._invitations.update((list) => [invitation, ...list]);
    return invitation;
  }
}
