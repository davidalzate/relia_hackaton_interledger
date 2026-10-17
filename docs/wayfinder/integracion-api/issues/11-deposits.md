# Integrar POST /agents/{agentId}/deposits

Type: task
Status: open
Blocked by: [07](07-operations-get.md), [10](10-keys-get.md), [09](09-operations-preview.md)
Labels: wayfinder:task, frontend, feature:depositar
Feature: Depositar (B2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /agents/{agentId}/deposits` + `Idempotency-Key`
- **Pantalla:** B2.4 Enviando → B2.5 / B2.6
- **Flujo:** [FLUJOS.md §5](../../../../contracts/FLUJOS.md#5-depositar)
- **Reemplaza:** `AgentService.deposit`

## Aceptación

- [ ] Body `{ phone, requestedAmount, roundAmount, cashReceivedConfirmed: true }` → 202.
- [ ] Polling hasta terminal; `completed` muestra `netAmount`, `fee`, `balances`.
- [ ] `422 insufficient_quota` → "Cupo insuficiente"; `503 rail_unavailable` → "El simulador no respondió. No se movió dinero."; `failed/payment_failed` → B2.6.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
