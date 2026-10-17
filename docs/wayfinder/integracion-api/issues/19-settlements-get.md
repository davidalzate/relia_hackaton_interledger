# Integrar GET /settlements/{settlementId}

Type: task
Status: claimed
PR: [#37](https://github.com/davidalzate/relia_hackaton_interledger/pull/37)
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:admin
Feature: Admin (D1–D3)

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

## Notas

- Polling en `AdminService.watchSettlement`.
- #37 se fusionó en `feat/receipt-api`, no en `main`: falta un PR de esa rama a `main`.
