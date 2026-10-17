# Integrar POST /demo/quota-top-ups/{operationId}/breb-payment

Type: task
Status: resolved
PR: [#31](https://github.com/davidalzate/relia_hackaton_interledger/pull/31)
Blocked by: [06](06-quota-top-ups.md)
Labels: wayfinder:task, frontend, feature:cargar-cupo
Feature: Cargar cupo (B6)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /demo/quota-top-ups/{operationId}/breb-payment`
- **Pantalla:** E1 Simulador Bre-B
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** `AgentService.confirmCupoLoad` disparado desde `DemoPanelComponent`

## Aceptación

- [ ] Botón "Simular pago recibido" llama al endpoint; la pantalla B6 lo ve por polling.
- [ ] Sin token; 404 si `DEMO_MODE` está apagado → ocultar panel.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- El panel Modo demo también enlaza a `approvalUrl` en cargas por Wallet; se quitó "Rechazar pago" simulado.
