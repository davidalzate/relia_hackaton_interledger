# 06: Desglose en vivo de depósito y retiro

Type: task
Status: open
Blocked by: 04
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** mientras el corresponsal escribe el monto (B2.1 y B3.1), ve la tarifa, el neto, lo cobrado y sus saldos después de la operación. Si la operación no se puede crear, el botón se deshabilita.

- [ ] `POST /agents/{agentId}/operations/preview` responde siempre 200 con `OperationPreview`.
- [ ] La tarifa sale de la tarifa vigente ($1.000 = 500 Relia + 500 corresponsal). Es una función pura de (tipo, monto, valor redondo, tarifa) → `fee`, `netAmount`, `chargedAmount`, que reutilizan depósitos y retiros.
- [ ] `roundAmount: true` → el usuario recibe el monto exacto y paga la tarifa aparte. `false` → la tarifa se descuenta.
- [ ] `quotaAfter`, `cashOnHandAfter` y `accruedCommissionAfter` tienen en cuenta las reservas.
- [ ] `violations`: `amount_too_low`, `insufficient_quota` (depósito) e `insufficient_cash` (retiro), con `shortfall`.
- [ ] Hay pruebas HTTP de los dos tipos, los dos modos de redondeo y cada violación.
