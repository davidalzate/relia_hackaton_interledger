# Integrar POST /demo/messages/read

Type: task
Status: open
Blocked by: [24](24-demo-messages.md)
Labels: wayfinder:task, frontend, feature:demo
Feature: Modo demo (E1, C2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /demo/messages/read`
- **Pantalla:** C2 al abrir el teléfono
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** Contador de no leídos local

## Aceptación

- [ ] Body `{ phone }` o vacío (marca todos) al abrir el teléfono.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
