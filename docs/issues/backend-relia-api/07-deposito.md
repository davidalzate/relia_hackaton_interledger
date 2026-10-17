# 07: Depósito de punta a punta

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal recibe efectivo y Relia paga el neto a la billetera del usuario por el riel. La operación se crea en `pending` y el frontend consulta su estado hasta un estado final. Este ticket construye piezas que reutilizan el retiro, la carga y la liquidación:

- la máquina de estados de la operación;
- la tabla de traducción del pago de riel;
- el refresco al leer;
- la transacción de completado en el orden que exige la base: pago `completed` → operación `completed` (el trigger congela el recibo) → movimiento y saldos → evento y mensaje.

`IRailClient` gana crear pago y consultar pago.

**Blocked by:** 03 (Ajustes de efectivo e idempotencia), 04 (Desglose en vivo), 06 (Cliente de riel y vinculación pública con OTP)

**Status:** ready-for-agent

- [ ] `POST /agents/{agentId}/deposits` (idempotente, `cashReceivedConfirmed: true`) responde `202` con la `Operation` `pending` y el header `Location`.
- [ ] Copia la tarifa y los montos del desglose. Reserva el monto cobrado de cupo. Si no alcanza, responde `422 insufficient_quota` con `shortfall`.
- [ ] Sin llave responde `404 key_not_found`. El admin recibe `403`.
- [ ] Crea el registro en `payments` y llama `POST /payments` outgoing al pointer del usuario, con `receiveAmount` = neto y `Idempotency-Key` = `payments.id`.
- [ ] Guarda `rail_payment_id`, las URLs del riel y la `version`.
- [ ] Si el proxy no está disponible o no tiene permiso (`relia_grant_unavailable`), responde `503 rail_unavailable` con rollback total.
- [ ] `GET /operations/{operationId}` refresca el pago desde el proxy, como máximo una vez por segundo por pago.
- [ ] El refresco descarta `version` ≤ `rail_version` y aplica la tabla de traducción de estados.
- [ ] Al completarse, en una transacción: el cupo baja el monto cobrado, el efectivo sube el monto cobrado, la comisión sube la parte del corresponsal, queda un movimiento en `ledger_entries`, el recibo queda congelado y se envía un SMS de aviso de depósito.
- [ ] Un `failed` deja la operación `failed`/`payment_failed` y libera la reserva, con `failureDetail` tomado del error del riel.
- [ ] Cada cambio de estado queda en `operation_events` y cada llamada al riel en `payment_events`.
- [ ] Un agente que lee una operación ajena recibe `403`.
- [ ] El depósito aparece en el historial con antes y después, y `v_agent_balance_check` sigue en 0.
- [ ] Verificado con la colección contra el proxy real y el permiso aprobado: depósito de $50.000 al `3000000001` hasta `completed`, cupo insuficiente, repetición con la misma clave, y el proxy apagado para el 503.
