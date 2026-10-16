# Integrar POST /operations/{operationId}/cash-handover

Type: task
Status: open
Blocked by: [12](12-withdrawals.md)
Labels: wayfinder:task, frontend

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
