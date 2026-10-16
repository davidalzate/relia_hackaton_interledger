# Integrar GET /settlements

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /settlements?from=<hoy America/Bogota>`
- **Pantalla:** D1 Pagos de comisión de hoy
- **Flujo:** [FLUJOS.md §10](../../../../contracts/FLUJOS.md#10-admin-panel-detalle-y-pago-de-comisión)
- **Reemplaza:** `AdminService.payments`

## Aceptación

- [ ] `from` = inicio del día en America/Bogota (no UTC del navegador).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
