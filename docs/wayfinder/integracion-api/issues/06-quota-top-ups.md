# Integrar POST /agents/{agentId}/quota-top-ups

Type: task
Status: claimed
PR: [#31](https://github.com/davidalzate/relia_hackaton_interledger/pull/31)
Blocked by: [07](07-operations-get.md)
Labels: wayfinder:task, frontend, feature:cargar-cupo
Feature: Cargar cupo (B6)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /agents/{agentId}/quota-top-ups` + `Idempotency-Key`
- **Pantalla:** B6 Cargar cupo
- **Flujo:** [FLUJOS.md §4](../../../../contracts/FLUJOS.md#4-cargar-cupo)
- **Reemplaza:** `AgentService.requestCupoLoad` / `confirmCupoLoad` / `failCupoLoad`

## Aceptación

- [ ] Bre-B: `{ method: "breB", requestedAmount }` → muestra `qrPayload` y contador de `expiresAt` (10 min).
- [ ] Wallet: `{ method: "wallet", requestedAmount }` → muestra `approvalUrl` (enlace/QR) y `expiresAt` (5 min).
- [ ] Polling con `GET /operations/{id}`; `completed` → "Cupo cargado" con `balances.quota.before/after`.
- [ ] Fallos → B6.5 según `failureCode`.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Estado y polling en `QuotaTopUpService`. Wallet ya no pide llave: el API cobra a la billetera registrada del corresponsal.
