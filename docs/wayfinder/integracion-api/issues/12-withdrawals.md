# Integrar POST /agents/{agentId}/withdrawals

Type: task
Status: open
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
