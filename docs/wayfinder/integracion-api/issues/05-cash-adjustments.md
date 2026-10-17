# Integrar POST /agents/{agentId}/cash-adjustments

Type: task
Status: claimed
PR: [#25](https://github.com/davidalzate/relia_hackaton_interledger/pull/25)
Blocked by: [03](03-agent-detail.md)
Labels: wayfinder:task, frontend, feature:base
Feature: Base y sesión

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /agents/{agentId}/cash-adjustments` + `Idempotency-Key`
- **Pantalla:** B7 Efectivo en caja
- **Flujo:** [FLUJOS.md §3](../../../../contracts/FLUJOS.md#3-efectivo-en-caja)
- **Reemplaza:** `AgentService.updateCash`

## Aceptación

- [ ] Modos `{ mode: "add", amount }` y `{ mode: "set", amount }`; 201 síncrono.
- [ ] "Antes → después" desde `cashOnHand.before/after`.
- [ ] Errores `422 insufficient_cash`, `422 no_change` con texto de pantalla.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
