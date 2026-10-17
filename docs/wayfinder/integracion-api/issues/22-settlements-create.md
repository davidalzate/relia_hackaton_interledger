# Integrar POST /agents/{agentId}/settlements

Type: task
Status: open
Blocked by: [19](19-settlements-get.md)
Labels: wayfinder:task, frontend, feature:admin
Feature: Admin (D1–D3)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /agents/{agentId}/settlements` + `Idempotency-Key`
- **Pantalla:** D3 Confirmar pago
- **Flujo:** [FLUJOS.md §10](../../../../contracts/FLUJOS.md#10-admin-panel-detalle-y-pago-de-comisión)
- **Reemplaza:** `PayCommissionComponent` / `CommissionPayment` (mock)

## Aceptación

- [ ] 202 `pending` → polling con `GET /settlements/{id}`.
- [ ] Errores `409 settlement_in_progress`, `422 no_commission` ("Sin comisión"), `503 rail_unavailable`.
- [ ] Refresca D1/D2 al terminar.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
