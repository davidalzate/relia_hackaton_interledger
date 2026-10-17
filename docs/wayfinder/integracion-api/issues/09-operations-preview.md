# Integrar POST /agents/{agentId}/operations/preview

Type: task
Status: resolved
PR: [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28)
Blocked by: [03](03-agent-detail.md)
Labels: wayfinder:task, frontend, feature:depositar
Feature: Depositar (B2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /agents/{agentId}/operations/preview`
- **Pantalla:** B2.1, B3.1 Desglose
- **Flujo:** [FLUJOS.md §5](../../../../contracts/FLUJOS.md#5-depositar)
- **Reemplaza:** `depositBreakdown` / `withdrawBreakdown` (cálculo local)

## Aceptación

- [ ] Body `{ type: "deposit"|"withdrawal", requestedAmount, roundAmount }` al cambiar monto o "valor redondo" (con debounce).
- [ ] Siempre 200; si `violations` no está vacío (`amount_too_low`, `insufficient_quota` con `shortfall`) deshabilita el botón.
- [ ] Decidir si se borran los cálculos locales o quedan solo como fallback.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Se borró `depositBreakdown`. Retirar sigue con `withdrawBreakdown` hasta su feature.
