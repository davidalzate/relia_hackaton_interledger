# 07: Depósito de punta a punta

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal recibe efectivo y Relia paga el neto a la billetera del usuario por el riel. La operación se crea en `pending` y el frontend consulta su estado hasta un estado final. Este ticket construye piezas que reutilizan el retiro, la carga y la liquidación:

- la máquina de estados de la operación;
- la tabla de traducción del pago de riel;
- el refresco al leer;
- la transacción de completado en el orden que exige la base: pago `completed` → operación `completed` (el trigger congela el recibo) → movimiento y saldos → evento y mensaje.

`IRailClient` gana crear pago y consultar pago.

**Blocked by:** 03 (Ajustes de efectivo e idempotencia), 04 (Desglose en vivo), 06 (Cliente de riel y vinculación pública con OTP)

**Status:** ready-for-human (implementado; probado contra un proxy falso y el proxy real; falta el depósito `completed` con el proxy real, que necesita un permiso de Relia con más tope, y correr la colección)

- [x] `POST /agents/{agentId}/deposits` (idempotente, `cashReceivedConfirmed: true`) responde `202` con la `Operation` `pending` y el header `Location`.
- [x] Copia la tarifa y los montos del desglose. Reserva el monto cobrado de cupo. Si no alcanza, responde `422 insufficient_quota` con `shortfall`.
- [x] Sin llave responde `404 key_not_found`. El admin recibe `403`.
- [x] Crea el registro en `payments` y llama `POST /payments` outgoing al pointer del usuario, con `receiveAmount` = neto y `Idempotency-Key` = `payments.id`.
- [x] Guarda `rail_payment_id`, las URLs del riel y la `version`.
- [x] Si el proxy no está disponible o no tiene permiso (`relia_grant_unavailable`), responde `503 rail_unavailable` con rollback total.
- [x] `GET /operations/{operationId}` refresca el pago desde el proxy, como máximo una vez por segundo por pago.
- [x] El refresco descarta `version` ≤ `rail_version` y aplica la tabla de traducción de estados.
- [x] Al completarse, en una transacción: el cupo baja el monto cobrado, el efectivo sube el monto cobrado, la comisión sube la parte del corresponsal, queda un movimiento en `ledger_entries`, el recibo queda congelado y se envía un SMS de aviso de depósito.
- [x] Un `failed` deja la operación `failed`/`payment_failed` y libera la reserva, con `failureDetail` tomado del error del riel.
- [x] Cada cambio de estado queda en `operation_events` y cada llamada al riel en `payment_events`.
- [x] Un agente que lee una operación ajena recibe `403`.
- [x] El depósito aparece en el historial con antes y después, y `v_agent_balance_check` sigue en 0.
- [ ] Verificado con la colección contra el proxy real y el permiso aprobado: depósito de $50.000 al `3000000001` hasta `completed`, cupo insuficiente, repetición con la misma clave, y el proxy apagado para el 503.

## Notas de implementación

- **`IRailClient`** gana `CreatePaymentAsync` (`Idempotency-Key` = `payments.id`; un `200` por idempotencia se trata igual que un `201`) y `GetPaymentAsync`.
- **`payments.id` determinista**: es el SHA-256 de (corresponsal, `deposit`, `Idempotency-Key` del frontend). Así un reintento después de un timeout pide al riel el mismo pago en vez de crear otro.
  - Por eso la descripción del pago ("Depósito en Droguería San José") no lleva el `OP-xxxxxx`: cambia en cada reintento y el proxy vería otro cuerpo con la misma clave.
  - Si el frontend reintenta la misma clave con otro cuerpo después de un `503`, el proxy responde `idempotency_conflict` y el backend `409 idempotency_conflict`.
- **`IOperationSync`** (`Services/OperationSync.cs`) es el sincronizador que van a reutilizar el retiro, la carga y el barrido:
  - `RailStatusTranslation` es la tabla de traducción de estados. Un estado desconocido deja la operación `pending`;
  - `ApplyAsync` registra toda lectura en `payment_events`, también las descartadas (`version` ≤ `rail_version`, marcadas "(descartada)") y los refrescos sin respuesta. Copia al pago `rail_payment_id`, las URLs del riel, `approval_url`, `expires_at`, `debit*`/`received*` y el error, y escribe un `payment_event` con la respuesta del proxy;
  - si el estado es final, completa o falla la operación;
  - al completar guarda en el orden que exigen los triggers: pago `completed` → operación `completed` (el trigger congela el recibo) → `ledger.Post` con la fila de saldos bloqueada → evento y SMS `deposit_notice`. Cada paso es un `SaveChanges` aparte dentro de la misma transacción;
  - el movimiento por tipo (depósito, retiro, carga) sigue la tabla de la spec;
  - si la operación ya terminó por otro camino y el riel cierra después, el pago refleja el estado del riel, no se mueven saldos y un `completed` queda como log de error para soporte;
  - un fallo, vencimiento o cancelación no escribe movimiento: la reserva se libera sola porque sale de las operaciones `pending`;
  - `RefreshAsync` consulta el proxy fuera de la transacción y después bloquea la operación y su último pago (`FOR UPDATE`) para aplicar. Así dos lecturas simultáneas no completan dos veces. Si el proxy no responde, no cambia nada.
- **Una consulta por segundo por pago**: `RailRefreshThrottle` (singleton en memoria). Olvida el pago cuando su operación termina.
- **Creación**: corre dentro de la transacción de `[Idempotent]`.
  - Bloquea `agent_balances`, valida la tarifa (`amount_too_low`) y el cupo disponible (cupo − reservado → `insufficient_quota` con `shortfall`).
  - Inserta la operación, el evento `∅→pending` y el pago, y guarda para obtener el `OP-xxxxxx` de la descripción ("Depósito OP-000018 · Droguería San José", recortada a 140).
  - Después llama a `POST /payments` y aplica la respuesta.
  - Riel caído o `relia_grant_unavailable` → `503 rail_unavailable`. `wallet_not_found`, `unsupported_asset` o `same_wallet` → `422 wallet_not_found`. En los dos casos el filtro hace rollback y no queda nada, ni registro de idempotencia.
  - Si la base fallara al confirmar *después* de que el proxy aceptó el pago, el reintento con la misma clave recupera ese pago (ver "`payments.id` determinista").
- **`CreateDepositRequest`**: `requestedAmount`, `roundAmount` y `cashReceivedConfirmed` son obligatorios de verdad. `cashReceivedConfirmed: false` → `422`, porque el contrato lo define como `const: true`.
- **`Operation.balances`**: si está completada, el antes y el después de su movimiento. Si no, antes = después con el último movimiento hasta `updated_at`, la misma regla que `v_agent_history`. `failureDetail` sale de `payments.error_message`; `approvalUrl` y `expiresAt`, del pago, solo mientras está `pending`.
- **`Mapping.FailureCodes`** es ahora compartido: `AgentService` usaba su propia copia.
- **Verificado**:
  - **Contra un proxy falso** (script de Python en el scratchpad, que responde `POST /payments` y deja cambiar el estado y la versión del pago):
    - $50.000 redondo → `202`, `Location`, `pending`, cliente enmascarado, `quotaReserved` 51.000;
    - la misma clave devuelve el mismo id y no crea otra operación; con otro cuerpo → `409 idempotency_conflict`;
    - el proxy recibe `outgoing` a `$…/jeffdev` por `{ "5000000", COP, 2 }`, con `Idempotency-Key` = `payments.id`;
    - `completed` v2 → la operación queda `completed` con cupo −51.000, efectivo +51.000 y comisión +500. Quedan el recibo (`OP-000018`), un `ledger_entry` de depósito, los `operation_events` `∅→pending(agent)` y `pending→completed(rail)`, los `payment_events` `create` y `refresh`, y el SMS `aviso_deposito` con su `operationId`;
    - $20.000 sin valor redondo y `failed` → `failed`/`payment_failed`, con `failureDetail` del riel y la reserva liberada;
    - `completed` con versión 1 (ya procesada) → sigue `pending`; con la v3 se completa;
    - dos `GET` seguidos → una sola consulta al proxy;
    - Diana leyendo una operación de Luis → `403`; el admin → `200`; un id inexistente → `404`;
    - el historial muestra los tres depósitos con antes y después;
    - `v_agent_balance_check` queda en 0 para los 4 corresponsales;
    - después de la revisión: el proxy crea el pago pero responde tarde (timeout de 1 s) → `503`. El reintento con la misma clave → `202` OP-000024, y el proxy tiene **un solo** pago con esa clave. La bitácora del pago queda `create:processing v1 | refresh:completed v1 (descartada) | refresh:completed v2`.
  - **Errores previos**: sin llave → `404 key_not_found`; Rosa sin cupo → `422 insufficient_quota` (`shortfall` 51.000); el admin → `403`; sin `roundAmount` o con `cashReceivedConfirmed: false` → `422`.
  - **Contra el proxy real**: el permiso de Relia está `exhausted`, porque su tope diario es `1000000` (= $10.000 COP). El proxy responde `503 relia_grant_unavailable` y el backend `503 rail_unavailable` sin crear nada. Con el proxy apagado → `503` dos veces con la misma clave, sin operación ni registro de idempotencia.

  Quedaron en la base local OP-000018, OP-000020 y OP-000024 (completados contra el proxy falso, que **no movieron dinero real**) y OP-000019 (`failed`). `POST /demo/reset` (ticket 14) o borrar el volumen los limpia.

