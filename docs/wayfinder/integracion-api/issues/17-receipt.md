# Integrar GET /operations/{operationId}/receipt

Type: task
Status: open
Blocked by: [07](07-operations-get.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /operations/{operationId}/receipt?channel=screen|print`
- **Pantalla:** B9 Comprobante
- **Flujo:** [FLUJOS.md §9](../../../../contracts/FLUJOS.md#9-historial-y-comprobante)
- **Reemplaza:** `ReceiptService.open` / `ReceiptDialogComponent` (datos locales)

## Aceptación

- [ ] `channel=screen` al abrir; `channel=print` al imprimir (registra emisión).
- [ ] Muestra "COPIA" si `isCopy: true`.
- [ ] `409 receipt_not_available` / `operation_not_completed` con su texto.
- [ ] Cargas, ajustes y liquidaciones: el front arma el comprobante con la operación/fila/liquidación.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
