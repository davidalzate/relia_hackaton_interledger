# 09: Depósito de punta a punta

Type: task
Status: resolved
Blocked by: 03, 05, 06, 07
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal recibe efectivo y Relia paga el neto a la billetera del usuario (flujo 5, B2). La operación se crea en `pending` y el frontend consulta su estado hasta un estado final. Este ticket construye la máquina de estados de la operación y el seguimiento de pagos de riel que reutilizan retiros, cargas y liquidaciones.

- [ ] `POST /agents/{agentId}/deposits` (con `Idempotency-Key`, `cashReceivedConfirmed: true`) responde 202 con `Operation` en `pending` y el header `Location`.
- [ ] Reserva `chargedAmount` de cupo. Si no alcanza, responde `422 insufficient_quota` con `shortfall`. Un celular sin llave responde `404 key_not_found`. El admin recibe `403`.
- [ ] Crea el pago (`pagos`) y llama `POST /payments` outgoing al pointer del usuario con `receiveAmount` = `netAmount`. Si el riel no está disponible, responde `503 rail_unavailable` y hace rollback total.
- [ ] `GET /operations/{operationId}` refresca el pago desde el proxy como máximo una vez por segundo por pago, descarta lecturas con `version` menor y aplica la tabla de traducción de estados de FLUJOS.md (Anexo).
- [ ] Al pasar a `completed`, en una transacción: baja el cupo, suben el efectivo y la comisión, se escribe el movimiento de cupo, se congela el recibo (contenido, código y hash) y se envía un SMS `aviso_deposito`.
- [ ] Un `failed` deja la operación `failed`/`payment_failed` y libera la reserva.
- [ ] Cada cambio de estado y cada respuesta del riel quedan en `eventos_operacion` y `eventos_pago`.
- [ ] El depósito aparece en el historial con antes y después.
- [ ] El agente no lee operaciones ajenas (403).
- [ ] Hay pruebas HTTP con el doble del riel para completed, failed, 503, cupo insuficiente, idempotencia y `version` vieja.
