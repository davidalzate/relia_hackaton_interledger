import { Injectable, signal } from '@angular/core';

export interface Sms {
  /** 10 dígitos */
  phone: string;
  text: string;
  operationId?: string;
  sentAt: string;
}

// shortcut: SMS simulados que se muestran en el teléfono del usuario (C2); no sale nada real
@Injectable({ providedIn: 'root' })
export class SmsService {
  /** Más recientes primero */
  private readonly _messages = signal<Sms[]>([]);
  readonly messages = this._messages.asReadonly();

  send(phone: string, text: string, operationId?: string): void {
    this._messages.update((list) => [
      { phone, text, operationId, sentAt: new Date().toISOString() },
      ...list,
    ]);
  }

  sentFor(operationId: string): boolean {
    return this._messages().some((m) => m.operationId === operationId);
  }
}
