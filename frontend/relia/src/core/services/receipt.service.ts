import { Injectable, signal } from '@angular/core';

/** Abre el comprobante (B9) como modal sobre la vista actual, desde cualquier pantalla */
@Injectable({ providedIn: 'root' })
export class ReceiptService {
  private readonly _operationId = signal<string | null>(null);
  readonly operationId = this._operationId.asReadonly();

  open(operationId: string): void {
    this._operationId.set(operationId);
  }

  close(): void {
    this._operationId.set(null);
  }
}
