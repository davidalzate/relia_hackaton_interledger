# Integrar POST /demo/operations/{operationId}/expire

Type: task
Status: open
Blocked by: [07](07-operations-get.md)
Labels: wayfinder:task, frontend, feature:demo
Feature: Modo demo (E1, C2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /demo/operations/{operationId}/expire`
- **Pantalla:** E1 "La solicitud vence ya"
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** `AgentService.failWithdrawal('expired')` / `failCupoLoad`

## Aceptación

- [ ] Aplica a retiros y cargas pendientes; la pantalla lo ve por polling.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
