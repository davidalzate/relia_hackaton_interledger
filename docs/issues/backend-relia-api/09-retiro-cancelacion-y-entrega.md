# 09: Retiro, cancelación y entrega de efectivo

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal le pide un pago a la billetera del usuario. El usuario aprueba en su wallet de prueba y, solo entonces, el corresponsal entrega el efectivo y confirma la entrega. Quien presenta ve la solicitud en "Mi billetera" del teléfono simulado. `IRailClient` gana cancelar pago.

**Blocked by:** 07 (Depósito de punta a punta)

**Status:** ready-for-agent

- [ ] `POST /agents/{agentId}/withdrawals` (idempotente) responde `202` con `approvalUrl` y `expiresAt` (+5 min). Reserva el neto del efectivo. Si no alcanza, responde `422 insufficient_cash` con `shortfall`.
- [ ] Llama `POST /payments` incoming con `counterparty` = pointer del usuario, `receiveAmount` = monto cobrado, `expiresAt` y una `description` legible.
- [ ] Envía un SMS de solicitud de pago con la `approvalUrl`.
- [ ] Traducción de estados: `rejected` → `failed`/`customer_rejected`; `expired` → `expired`/`request_expired`; `cancelled` → `cancelled`/`request_cancelled`; `failed` → `failed`/`payment_failed`. Cada uno libera la reserva y deja `failure_code`.
- [ ] Al completarse: el cupo sube el neto, el efectivo baja el neto, la comisión sube la parte del corresponsal y el recibo queda congelado.
- [ ] `POST /operations/{operationId}/cancel` responde `200` antes de la aprobación y cancela en el riel. Después, o si el proxy devuelve `payment_not_cancellable`, responde `409 operation_not_cancellable`.
- [ ] `POST /operations/{operationId}/cash-handover` llena `cashHandedOverAt` y deja un evento de auditoría sin tocar saldos. Repetirlo es idempotente. Sobre un retiro no completado responde `409 operation_not_completed`.
- [ ] El admin recibe `403` al crear, cancelar y confirmar la entrega.
- [ ] `GET /demo/payment-requests?phone=` lista las solicitudes pendientes de ese celular con `approvalUrl`, `agentName` y `chargedAmount`.
- [ ] Verificado contra la red de prueba con una persona aprobando: retiro de $100.000 aprobado, otro rechazado, otro cancelado antes de aprobar, cancelar después de aprobar (409) y la entrega confirmada dos veces. `v_agent_balance_check` queda en 0.
