# Integrar POST /operations/{operationId}/receipt/send

Type: task
Status: resolved
PR: [#33](https://github.com/davidalzate/relia_hackaton_interledger/pull/33)
Blocked by: [17](17-receipt.md)
Labels: wayfinder:task, frontend, feature:comprobante
Feature: Comprobante (B9)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /operations/{operationId}/receipt/send`
- **Pantalla:** B9 Enviar SMS
- **Flujo:** [FLUJOS.md §9](../../../../contracts/FLUJOS.md#9-historial-y-comprobante)
- **Reemplaza:** `SmsService.send` / `sentFor`

## Aceptación

- [ ] Body `{ channel: "sms" }` → 202.
- [ ] El SMS aparece en `GET /demo/messages` (se borra el SMS local).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Se quitó el SMS armado en el front; el botón aparece solo con recibo oficial.
