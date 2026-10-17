# Integrar GET /demo/payment-requests

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:demo
Feature: Modo demo (E1, C2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /demo/payment-requests?phone=`
- **Pantalla:** C2 Mi billetera
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** Botones aprobar/rechazar mock

## Aceptación

- [ ] Lista solicitudes pendientes con enlace a `approvalUrl` (se aprueba en la wallet real).
- [ ] Se eliminan los botones de aprobar/rechazar simulados.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
