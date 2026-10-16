# Integrar GET /operations/{operationId}

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /operations/{operationId}`
- **Pantalla:** B2.4, B3.2, B6, B9
- **Flujo:** [FLUJOS.md §5](../../../../contracts/FLUJOS.md#5-depositar)
- **Reemplaza:** `AgentService.operation(id)` (lookup local)

## Aceptación

- [ ] Polling 1–2 s hasta `completed|failed|expired|cancelled` usando el helper de base.
- [ ] Mapea `failureCode` a textos (anexo de estados).
- [ ] Se detiene al salir de la pantalla (sin fugas de suscripción).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
