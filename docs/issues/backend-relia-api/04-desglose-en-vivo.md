# 04: Desglose en vivo de depósito y retiro

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** mientras el corresponsal escribe el monto, ve la tarifa, el neto, lo cobrado y sus saldos después de la operación. Si la operación no se puede crear, el botón se deshabilita. El cálculo es una función pura de (tipo, monto, valor redondo, tarifa vigente) que después reutilizan el depósito y el retiro.

**Blocked by:** 02 (Caja: saldos e historial)

**Status:** ready-for-agent

- [ ] `POST /agents/{agentId}/operations/preview` responde siempre `200` con `OperationPreview`.
- [ ] La tarifa sale de la fila vigente de `fees` ($1.000 = 500 Relia + 500 corresponsal).
- [ ] `roundAmount: true` → `netAmount` = monto y `chargedAmount` = monto + tarifa. `false` → `netAmount` = monto − tarifa y `chargedAmount` = monto.
- [ ] Depósito: `quotaAfter` = cupo − reservado − cobrado y `cashOnHandAfter` = efectivo + cobrado.
- [ ] Retiro: `quotaAfter` = cupo + neto y `cashOnHandAfter` = efectivo − reservado − neto.
- [ ] En los dos tipos, `accruedCommissionAfter` = comisión acumulada + parte del corresponsal. (Son las mismas reglas que valida la base.)
- [ ] `violations`: `amount_too_low` (monto ≤ tarifa o ≤ $1.000), `insufficient_quota` en depósito e `insufficient_cash` en retiro, con `shortfall`.
- [ ] El admin recibe `403`.
- [ ] Verificado con la carpeta `operations/preview` en los dos tipos, los dos modos de redondeo y cada violación (por ejemplo, con Rosa en ceros).
