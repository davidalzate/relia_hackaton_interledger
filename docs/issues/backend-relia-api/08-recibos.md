# 08: Recibos de depósito y retiro

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal ve, imprime o envía por SMS o WhatsApp el recibo congelado de una operación completada. La base ya congela el recibo y marca las copias. El backend traduce el `data` congelado (snake_case, datos completos) al `ReceiptData` del contrato, lo enmascara y registra cada emisión.

**Blocked by:** 07 (Depósito de punta a punta)

**Status:** ready-for-human (implementado y probado con `curl`; falta correr la colección)

- [x] `GET /operations/{operationId}/receipt?channel=screen|print|pdf` devuelve el `Receipt` con `code` (12 hex), `contentHash`, `issuedAt` y `data` enmascarada, y registra una emisión con `requested_by` = `agent`.
- [x] Desde la segunda emisión, `isCopy` es true.
- [x] `payment.rail` sale como `openPayments` o `breBSimulated`, y `payment.reference` como el identificador del riel.
- [x] Una carga de cupo responde `409 receipt_not_available`. Una operación no completada responde `409 operation_not_completed`.
- [x] `POST /operations/{operationId}/receipt/send` `{ channel: sms|whatsapp }` responde `202` con `ReceiptDelivery` (`sentTo` enmascarado, `isCopy`). Deja un mensaje `receipt` (`comprobante` en la API) en la bandeja del usuario y registra una emisión.
- [x] El admin puede leer recibos. Un agente ajeno recibe `403`.
- [ ] Verificado con la carpeta `receipt` sobre el depósito del ticket 07: pantalla, segunda lectura (copia) y envío por SMS visible en `/demo/messages`.

## Notas de implementación

- **`ReceiptService`** (`Services/ReceiptService.cs`) lee el `data` congelado por `issue_receipt` con un modelo privado en snake_case (`FrozenReceipt`) y lo traduce a `ReceiptData`:
  - `number` → `reference`;
  - `date` (hora de Bogotá sin zona) → `date-time` con `-05:00`;
  - `user.mobile` / `user.payment_pointer` → `customer` enmascarado;
  - `amounts.commission` → `fee`, y `round_value` → `roundAmount`;
  - `payment.rail` `open_payments` / `bre_b_simulated` → `openPayments` / `breBSimulated`;
  - `code`, `contentHash` e `issuedAt` (`receipts.created_at`) salen de la fila.
- **Orden de errores**: operación inexistente → `404`; otro corresponsal → `403`; carga de cupo → `409 receipt_not_available`, aunque esté pendiente; no completada → `409 operation_not_completed`.
- **Datos congelados inconsistentes**: un depósito o retiro completado sin fila en `receipts`, o un `type`, `rail` o `reference` inesperado (incluido `null`), lanza error interno (`500`). No los oculta tras un `409` ni los convierte a un valor por defecto. `date` se lee con el formato exacto de `issue_receipt`.
- **Emisiones**: cada `GET` y cada envío insertan una fila en `receipt_issuances`. `is_copy` lo calcula el trigger `mark_receipt_copy` y EF lo lee de vuelta al insertar.
  - Con el corresponsal se guarda `requested_by` = `agent` y su `agent_id`.
  - El admin no tiene corresponsal y `ck_issuance_requester` exige `agent_id` con `agent`, así que su emisión queda como `system`. También cuenta para la copia. Esto se aparta del texto "`requested_by` = `agent`" de la spec; hay que decidir si se ajusta la spec.
- **Envío**: el SMS o WhatsApp (`receipt` → `comprobante`) va al celular congelado en el recibo, con el `operationId`. Lleva "(COPIA)" si la emisión es copia, más el monto, el monto cobrado, la comisión y el código. `sentTo` sale enmascarado y `sentAt` es la fecha del mensaje.
  - El admin también puede enviar: el controlador no restringe el rol y aplica la regla de lectura. El contrato no lo prohíbe; queda como decisión de producto.
- **`SendReceiptRequest.channel`** es nullable con `[Required]`: sin canal responde `422`, en lugar de tomar `sms` por defecto.
- **Verificado con `curl`** sobre OP-000018 (depósito del ticket 07):
  - la primera lectura, de una operación sin emisiones, da `isCopy: false`; la siguiente (`print`) da `isCopy: true`;
  - el admin (`pdf`) → `200`, emisión `system`; Diana → `403`;
  - OP-000019 (`failed`) → `409 operation_not_completed`; una carga de cupo sembrada → `409 receipt_not_available`;
  - id inexistente → `404`; `channel=fax` → `422`;
  - enviar por `sms` y `whatsapp` → `202` con `sentTo` `300 *** 0001`; sin canal → `422`;
  - los dos mensajes aparecen en `/demo/messages?phone=3000000001` como `comprobante`;
  - las seis emisiones quedaron registradas, y `receipt_is_intact` devuelve `true`;
  - OP-000020: `code` de 12 hex, `isCopy: false` y `date` `2026-10-16T23:07:47-05:00` (frente a `created_at` 04:07 UTC).

