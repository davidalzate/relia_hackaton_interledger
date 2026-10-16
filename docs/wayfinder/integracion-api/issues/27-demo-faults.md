# Integrar GET/PUT /demo/faults

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /demo/faults`, `PUT /demo/faults`
- **Pantalla:** E1 "El próximo pago falla"
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** Interruptor local en `DemoPanelComponent`

## Aceptación

- [ ] `PUT { failNextPayment: true }`; el toggle refleja `GET /demo/faults`.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
