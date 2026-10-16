# Integrar GET /settlements/{settlementId}

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /settlements/{settlementId}`
- **Pantalla:** D3 Enviando, B8/D2 abrir fila
- **Flujo:** [FLUJOS.md §10](../../../../contracts/FLUJOS.md#10-admin-panel-detalle-y-pago-de-comisión)
- **Reemplaza:** `AdminService.payments` (señal local)

## Aceptación

- [ ] Polling hasta `paid` o `voided`.
- [ ] `voided` con `voidReason` → "El pago falló. No se movió dinero."
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
