# 12: Carga de cupo por Bre-B y por Wallet

Type: task
Status: resolved
Blocked by: 11
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal carga cupo con un QR Bre-B simulado o cobrándole a su propia billetera (flujo 4, B6).

- [ ] `POST /agents/{agentId}/quota-top-ups` `{ method: breB }` responde 202 con `qrPayload` y `expiresAt` (+10 min), sin tocar el riel.
- [ ] `POST /demo/quota-top-ups/{operationId}/breb-payment` completa la carga y sube el cupo. Si la operación no es una carga Bre-B pendiente, responde 409.
- [ ] `{ method: wallet }` llama `POST /payments` incoming al `walletAddress` del corresponsal y responde 202 con `approvalUrl` y `expiresAt` (+5 min).
- [ ] La carga completada sube el cupo con `fee` en 0 y `netAmount` = `chargedAmount` = monto. No tiene recibo.
- [ ] Cancelar y vencer se comportan como en el retiro.
- [ ] La carga aparece en `GET /demo/payment-requests?phone=<celular del corresponsal>` y en el historial como `quotaTopUp`.
- [ ] El admin recibe 403.
- [ ] Hay pruebas HTTP de los dos métodos, su completado, cancelación y vencimiento.
