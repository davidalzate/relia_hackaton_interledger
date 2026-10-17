# Integrar POST /operations/{operationId}/cash-handover

Type: task
Status: claimed
PR: [#29](https://github.com/davidalzate/relia_hackaton_interledger/pull/29)
Blocked by: [12](12-withdrawals.md)
Labels: wayfinder:task, frontend, feature:retirar
Feature: Retirar (B3)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /operations/{operationId}/cash-handover`
- **Pantalla:** B3.3 → B4 "Sí, confirmo"
- **Flujo:** [FLUJOS.md §6](../../../../contracts/FLUJOS.md#6-retirar)
- **Reemplaza:** Confirmación solo en UI

## Aceptación

- [ ] Se llama al confirmar la entrega de efectivo; no cambia saldos.
- [ ] Tras éxito, ofrece comprobante (B9).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Solo vuelve a Caja cuando el API confirma la entrega.
