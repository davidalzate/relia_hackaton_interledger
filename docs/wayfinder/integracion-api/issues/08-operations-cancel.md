# Integrar POST /operations/{operationId}/cancel

Type: task
Status: claimed
PR: [#27](https://github.com/davidalzate/relia_hackaton_interledger/pull/27)
Blocked by: [07](07-operations-get.md)
Labels: wayfinder:task, frontend, feature:operaciones
Feature: Seguimiento de operaciones

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /operations/{operationId}/cancel`
- **Pantalla:** B3.2 Cancelar solicitud, B6 Cancelar
- **Flujo:** [FLUJOS.md §6](../../../../contracts/FLUJOS.md#6-retirar)
- **Reemplaza:** `AgentService.clearWithdrawal` / `clearCupoLoad`

## Aceptación

- [ ] Cancela retiros y cargas pendientes.
- [ ] `409 operation_not_cancellable` → informa que ya fue aprobada y sigue el polling.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Servicio listo (`OperationsService.cancel`); los botones de B3.2 y B6 se conectan en Retirar y Cargar cupo.
