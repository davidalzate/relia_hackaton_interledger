# 10: Carga de cupo por Bre-B y por Wallet

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal carga cupo con un QR Bre-B simulado o cobrándole a su propia billetera registrada. En Bre-B, quien presenta marca el pago como recibido desde el Modo demo.

**Blocked by:** 09 (Retiro, cancelación y entrega de efectivo)

**Status:** ready-for-agent

- [ ] `POST /agents/{agentId}/quota-top-ups` `{ method: breB }` responde `202` con `qrPayload` simulado y `expiresAt` (+10 min).
- [ ] La carga Bre-B no llama al riel: crea un registro en `payments` con riel `bre_b_simulated`, marcado como simulado.
- [ ] `POST /demo/quota-top-ups/{operationId}/breb-payment` completa el pago y la carga, y el cupo sube. Si la operación no es una carga Bre-B pendiente, responde `409`.
- [ ] `{ method: wallet }` llama `POST /payments` incoming al `walletAddress` del corresponsal y responde `202` con `approvalUrl` y `expiresAt` (+5 min).
- [ ] La carga tiene `fee` en 0 y `netAmount` = `chargedAmount` = monto, sin `customer`, con `topUpMethod`.
- [ ] Completada, sube el cupo sin tocar efectivo ni comisión. No tiene recibo.
- [ ] Cancelar se comporta igual que en el retiro.
- [ ] La carga Wallet aparece en `GET /demo/payment-requests?phone=<celular del corresponsal>`.
- [ ] La carga aparece en el historial como `quotaTopUp`.
- [ ] El admin recibe `403`.
- [ ] Verificado con la carpeta `quota-top-ups`: Bre-B completada desde el demo, Wallet aprobada en la red de prueba y una cancelada.
