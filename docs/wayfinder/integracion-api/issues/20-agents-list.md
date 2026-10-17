# Integrar GET /agents

Type: task
Status: resolved
PR: [#37](https://github.com/davidalzate/relia_hackaton_interledger/pull/37) · [#41](https://github.com/davidalzate/relia_hackaton_interledger/pull/41)
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:admin
Feature: Admin (D1–D3)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /agents`
- **Pantalla:** D1 Inicio admin
- **Flujo:** [FLUJOS.md §10](../../../../contracts/FLUJOS.md#10-admin-panel-detalle-y-pago-de-comisión)
- **Reemplaza:** `AdminService.agents` / `totals` (`DEMO_AGENTS`)

## Aceptación

- [ ] `items` → tabla de corresponsales; `totals` → tarjetas, en una sola llamada.
- [ ] Se elimina `DEMO_AGENTS` y el cálculo local de totales.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Se borraron `DEMO_AGENTS` y la fila en vivo del front. D2 (detalle) se construyó con `GET /agents/{id}` e historial.
- #37 se fusionó en `feat/receipt-api`, no en `main`: falta un PR de esa rama a `main`.
