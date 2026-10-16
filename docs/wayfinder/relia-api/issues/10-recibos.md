# 10: Recibos de depósito y retiro

Type: task
Status: open
Blocked by: 09
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal ve, imprime o envía por SMS el recibo congelado de una operación completada (B9, flujo 9).

- [ ] `GET /operations/{operationId}/receipt?channel=screen|print|pdf` devuelve el `Receipt` congelado (`code` de 12 hex, `contentHash` SHA-256, `data` enmascarada) y registra una emisión.
- [ ] Desde la segunda emisión, `isCopy` es true.
- [ ] Una carga de cupo responde `409 receipt_not_available`. Una operación no completada responde `409 operation_not_completed`.
- [ ] `POST /operations/{operationId}/receipt/send` `{ channel: sms|whatsapp }` responde 202 con `ReceiptDelivery`, deja un mensaje `comprobante` en la bandeja y registra una emisión.
- [ ] El admin puede leer recibos. Un agente ajeno recibe 403.
- [ ] Hay pruebas HTTP de cada caso.
