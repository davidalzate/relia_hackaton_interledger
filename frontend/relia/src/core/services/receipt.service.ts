import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';

import { environment } from '../../environments/environment';
import { Receipt, ReceiptSchema } from '../../shared/schemas/operation.schema';

export type ReceiptChannel = 'screen' | 'print';

/** Abre el comprobante (B9) como modal sobre la vista actual, desde cualquier pantalla */
@Injectable({ providedIn: 'root' })
export class ReceiptService {
  private readonly http = inject(HttpClient);
  private readonly _operationId = signal<string | null>(null);
  readonly operationId = this._operationId.asReadonly();

  open(operationId: string): void {
    this._operationId.set(operationId);
  }

  close(): void {
    this._operationId.set(null);
  }

  /** Cada llamada registra una emisión; desde la segunda `isCopy` es true. Solo depósitos y retiros `completed` */
  get(operationId: string, channel: ReceiptChannel): Observable<Receipt> {
    return this.http
      .get(`${environment.apiUrl}/operations/${operationId}/receipt`, { params: { channel } })
      .pipe(
        timeout(environment.httpTimeoutMs),
        map((res) => ReceiptSchema.parse(res)),
      );
  }

  /** SMS simulado al usuario: queda en la bandeja del teléfono (`GET /demo/messages`) */
  sendSms(operationId: string): Observable<unknown> {
    return this.http
      .post(`${environment.apiUrl}/operations/${operationId}/receipt/send`, { channel: 'sms' })
      .pipe(timeout(environment.httpTimeoutMs));
  }
}
