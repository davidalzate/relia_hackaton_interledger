# 08: Recibos de depósito y retiro

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal ve, imprime o envía por SMS o WhatsApp el recibo congelado de una operación completada. La base ya congela el recibo y marca las copias. El backend traduce el `data` congelado (snake_case, datos completos) al `ReceiptData` del contrato, lo enmascara y registra cada emisión.

**Blocked by:** 07 (Depósito de punta a punta)

**Status:** ready-for-agent

- [ ] `GET /operations/{operationId}/receipt?channel=screen|print|pdf` devuelve el `Receipt` con `code` (12 hex), `contentHash`, `issuedAt` y `data` enmascarada, y registra una emisión con `requested_by` = `agent`.
- [ ] Desde la segunda emisión, `isCopy` es true.
- [ ] `payment.rail` sale como `openPayments` o `breBSimulated`, y `payment.reference` como el identificador del riel.
- [ ] Una carga de cupo responde `409 receipt_not_available`. Una operación no completada responde `409 operation_not_completed`.
- [ ] `POST /operations/{operationId}/receipt/send` `{ channel: sms|whatsapp }` responde `202` con `ReceiptDelivery` (`sentTo` enmascarado, `isCopy`). Deja un mensaje `receipt` (`comprobante` en la API) en la bandeja del usuario y registra una emisión.
- [ ] El admin puede leer recibos. Un agente ajeno recibe `403`.
- [ ] Verificado con la carpeta `receipt` sobre el depósito del ticket 07: pantalla, segunda lectura (copia) y envío por SMS visible en `/demo/messages`.
