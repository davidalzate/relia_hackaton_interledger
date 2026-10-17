# 04: Desglose en vivo de depósito y retiro

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** mientras el corresponsal escribe el monto, ve la tarifa, el neto, lo cobrado y sus saldos después de la operación. Si la operación no se puede crear, el botón se deshabilita. El cálculo es una función pura de (tipo, monto, valor redondo, tarifa vigente) que después reutilizan el depósito y el retiro.

**Blocked by:** 02 (Caja: saldos e historial)

**Status:** ready-for-human (implementado; falta correr la colección de Postman)

- [x] `POST /agents/{agentId}/operations/preview` responde siempre `200` con `OperationPreview`.
- [x] La tarifa sale de la fila vigente de `fees` ($1.000 = 500 Relia + 500 corresponsal).
- [x] `roundAmount: true` → `netAmount` = monto y `chargedAmount` = monto + tarifa. `false` → `netAmount` = monto − tarifa y `chargedAmount` = monto.
- [x] Depósito: `quotaAfter` = cupo − reservado − cobrado y `cashOnHandAfter` = efectivo + cobrado.
- [x] Retiro: `quotaAfter` = cupo + neto y `cashOnHandAfter` = efectivo − reservado − neto.
- [x] En los dos tipos, `accruedCommissionAfter` = comisión acumulada + parte del corresponsal. (Son las mismas reglas que valida la base.)
- [x] `violations`: `amount_too_low` (monto ≤ tarifa o ≤ $1.000), `insufficient_quota` en depósito e `insufficient_cash` en retiro, con `shortfall`.
- [x] El admin recibe `403`.
- [ ] Verificado con la carpeta `operations/preview` en los dos tipos, los dos modos de redondeo y cada violación (por ejemplo, con Rosa en ceros).

## Notas de implementación

- **`OperationRules`** (sin acceso a datos):
  - `Calculate` da tarifa, neto y cobrado; el depósito y el retiro lo van a usar para guardar los montos de la operación.
  - `Preview` agrega los saldos después y las violaciones. `shortfall` es lo que falta para que el saldo después quede en 0.
- **Valores fuera de rango**: con una violación, los números se devuelven igual, aunque salgan negativos (`quotaAfter`, `cashOnHandAfter`, o `netAmount` si el monto es menor que la tarifa). El frontend muestra la violación y deshabilita el botón.
- **Tope y `shortfall`**:
  - Todo monto de entrada tiene un tope de $1 billón (`Patterns.MaxCop`); por encima responde `422`. Sin tope, `long.MaxValue` desbordaba y escondía la violación.
  - `shortfall` se omite cuando no aplica (`amount_too_low`) en vez de salir `null`, porque el contrato lo define como entero.
- **Campos obligatorios**: `type`, `requestedAmount` y `roundAmount` lo son de verdad (nullable + `[Required]`). Si falta alguno, responde `422 validation_error`.
- **Corresponsal ajeno o admin**: `403`. Usa la misma regla de operar que los ajustes.
- **Verificado con `curl`**:
  - Luis: depósito y retiro, con y sin valor redondo, y `insufficient_cash` (`shortfall` 67.000);
  - Rosa: `insufficient_quota`, y `amount_too_low` con 1.000 (con 1.001 ya no aparece);
  - con un depósito pendiente de 30.000 insertado por SQL, `quotaAfter` descuenta la reserva y el `shortfall` la tiene en cuenta;
  - admin y corresponsal ajeno → 403; campos ausentes → 422.

  La operación de prueba (OP-000014 de Luis) quedó `cancelled`: la base no deja borrar operaciones.
