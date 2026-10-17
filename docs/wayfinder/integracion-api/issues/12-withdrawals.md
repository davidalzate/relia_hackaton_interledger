# Integrar POST /agents/{agentId}/withdrawals

Type: task
Status: resolved
PR: [#29](https://github.com/davidalzate/relia_hackaton_interledger/pull/29)
Blocked by: [07](07-operations-get.md), [10](10-keys-get.md), [09](09-operations-preview.md)
Labels: wayfinder:task, frontend, feature:retirar
Feature: Retirar (B3)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /agents/{agentId}/withdrawals` + `Idempotency-Key`
- **Pantalla:** B3.1 → B3.2 → B3.3 / B3.4
- **Flujo:** [FLUJOS.md §6](../../../../contracts/FLUJOS.md#6-retirar)
- **Reemplaza:** `AgentService.requestWithdrawal` / `approveWithdrawal` / `failWithdrawal`

## Aceptación

- [ ] Body `{ phone, requestedAmount, roundAmount }` → 202 con `approvalUrl` y `expiresAt`.
- [ ] B3.2 con contador desde `expiresAt` (no `WITHDRAWAL_TTL_MS` local).
- [ ] Errores B3.4 por `failureCode`: `customer_rejected`, `request_expired`, `request_cancelled`, `payment_failed`; al crear `422 insufficient_cash`.
- [ ] Se elimina `approveWithdrawal` mock: se aprueba en la wallet de prueba.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Estado y polling en `WithdrawalService` (sobrevive a abrir C2). B3.1 usa el preview del API; se borró `withdrawBreakdown`. C2 enlaza a `approvalUrl` en vez de Aprobar/Rechazar simulados.
- Cambio de flujo: ya no se envía la solicitud al teléfono del usuario. B3.2 muestra la `approvalUrl` como código QR (`QrCodeComponent`) para que el usuario la escanee y apruebe en su billetera; C2 ya no muestra la solicitud.
