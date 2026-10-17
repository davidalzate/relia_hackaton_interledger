# 10: Carga de cupo por Bre-B y por Wallet

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal carga cupo con un QR Bre-B simulado o cobrándole a su propia billetera registrada. En Bre-B, quien presenta marca el pago como recibido desde el Modo demo.

**Blocked by:** 09 (Retiro, cancelación y entrega de efectivo)

**Status:** ready-for-human (implementado; probado contra un proxy falso y contra el proxy real (crear y cancelar). Falta aprobar una carga Wallet en la red de prueba y correr la colección)

- [x] `POST /agents/{agentId}/quota-top-ups` `{ method: breB }` responde `202` con `qrPayload` simulado y `expiresAt` (+10 min).
- [x] La carga Bre-B no llama al riel: crea un registro en `payments` con riel `bre_b_simulated`, marcado como simulado.
- [x] `POST /demo/quota-top-ups/{operationId}/breb-payment` completa el pago y la carga, y el cupo sube. Si la operación no es una carga Bre-B pendiente, responde `409`.
- [x] `{ method: wallet }` llama `POST /payments` incoming al `walletAddress` del corresponsal y responde `202` con `approvalUrl` y `expiresAt` (+5 min).
- [x] La carga tiene `fee` en 0 y `netAmount` = `chargedAmount` = monto, sin `customer`, con `topUpMethod`.
- [x] Completada, sube el cupo sin tocar efectivo ni comisión. No tiene recibo.
- [x] Cancelar se comporta igual que en el retiro.
- [x] La carga Wallet aparece en `GET /demo/payment-requests?phone=<celular del corresponsal>`.
- [x] La carga aparece en el historial como `quotaTopUp`.
- [x] El admin recibe `403`.
- [ ] Verificado con la carpeta `quota-top-ups`: Bre-B completada desde el demo, Wallet aprobada en la red de prueba y una cancelada.

## Notas de implementación

- **Creación** (`[Idempotent]`): inserta una operación `float_load` sin llave ni tarifa (neto = cobrado = monto), su evento `∅→pending` y el pago. No reserva nada: el cupo solo sube al completarse.
  - **Bre-B**:
    - pago `bre_b_simulated` `incoming` de la billetera del corresponsal a la de Relia, con `expires_at` = ahora + 10 min;
    - `rail_details` = `{ "simulated": true, "qrPayload": "breb://relia.co/pay?ref=OP-…&amount=…&expires=…" }` y `rail_reference` = `BREB-OP-…`;
    - no llama al proxy; `qrPayload` se muestra mientras la operación está pendiente.
  - **Wallet**: igual que el retiro. `POST /payments` `incoming` con `counterparty` = `agents.payment_pointer`, el monto como `receiveAmount`, el plazo redondeado al minuto (5–6 min) y la descripción "Carga de cupo de $ 300.000 para Droguería San José".
    - No se envía SMS: el corresponsal recibe la `approvalUrl` en la respuesta, y en el demo la ve en "Mi billetera" con su celular.
    - Un corresponsal con `walletSimulated` (Luis, Diana, Jorge) tiene un pointer que puede no existir en el riel. Con el proxy real, su carga Wallet probablemente responde `422 wallet_not_found`. Solo Rosa tiene billetera real. La spec solo define el atajo simulado para la liquidación: **decisión de producto pendiente** si estos corresponsales deben poder cargar por Wallet.
- **`POST /demo/quota-top-ups/{id}/breb-payment`** (`[DemoOnly]`):
  - bloquea la operación y su pago, y registra un `payment_event` `simulate` de origen `simulator`;
  - completa con `IOperationSync.CompleteAsync` (ahora público, con el origen del evento), en el mismo orden de triggers que el riel;
  - si no es una carga Bre-B pendiente, o su QR ya venció (`expires_at` ≤ ahora, aunque el barrido del ticket 11 aún no la haya cerrado) → `409 operation_not_cancellable`. El contrato no nombra un código para este caso; es el más cercano del enum ("ya no se puede actuar sobre ella"). **Por confirmar.**
- **Cancelar**: el mismo `POST /operations/{id}/cancel` del ticket 09. Bre-B (sin id del proxy) se cancela localmente con `CloseWithoutMovementAsync`; Wallet se cancela en el riel.
- **Recibo**: `409 receipt_not_available` (ticket 08). **Historial**: `quotaTopUp` (ticket 02).
- **Verificado**:
  - **Contra un proxy falso**, con Luis:
    - Bre-B $200.000 → `202` con `fee` 0, neto = cobrado, `customer` `null`, `topUpMethod: breB`, `qrPayload` y `expiresAt` +10 min, sin reservas. La misma clave da la misma operación;
    - el pago quedó `bre_b_simulated`, `incoming`, `BREB-OP-000041`, `simulated: true`;
    - simular el pago → `completed`, cupo +200.000, movimiento `float_load` con efectivo y comisión en 0, y eventos `∅→pending(agent)` y `pending→completed(simulator)`. Simular otra vez → `409`; recibo → `409 receipt_not_available`;
    - Bre-B cancelada → `cancelled`/`request_cancelled`; simular sobre ella → `409`;
    - Wallet $300.000 → `202` con `approvalUrl` y `expiresAt`. El proxy recibió `incoming` de `$…/drogueria-san-jose` por `30000000` con la descripción, y aparece en "Mi billetera" de `3105550102`. Simular Bre-B sobre ella → `409`. Al completar → cupo +300.000;
    - Wallet cancelada → `cancelled` también en el proxy;
    - el admin → `403`; sin `method` o con monto 0 → `422`; simular sobre un id inexistente → `404`, sobre un depósito → `409`;
    - el historial muestra las cuatro cargas como `quotaTopUp`;
    - descuadres en 0.
  - **Después de la revisión**: el guion completo se repitió sin cambios (OP-000046 a OP-000049). Un QR Bre-B vencido (forzado por SQL) → simular da `409` y cancelarlo da `200`.
  - **Contra el proxy real**, con Rosa (`$…/rosa-test`): carga Wallet de $10.000 → `202` con un `approvalUrl` real; aparece en "Mi billetera" de `3105550101`; cancelar → `cancelled`/`request_cancelled`.
  - **Pendiente**: aprobar una carga Wallet en la wallet de prueba de Rosa.

  Quedaron en la base local OP-000041 a OP-000044 y OP-000046 a OP-000050 de Luis (contra el proxy falso) y el OP-000045 real de Rosa, cancelado.

