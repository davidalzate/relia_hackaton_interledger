# 11: Retiro de punta a punta

Type: task
Status: open
Blocked by: 09
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal pide un pago a la billetera del usuario, el usuario aprueba en su wallet de prueba y, solo entonces, el corresponsal entrega el efectivo y confirma la entrega (flujo 6, B3).

- [ ] `POST /agents/{agentId}/withdrawals` responde 202 con `approvalUrl` y `expiresAt` (+5 min). Reserva `netAmount` de efectivo. Si no alcanza, responde `422 insufficient_cash` con `shortfall`.
- [ ] Llama `POST /payments` incoming con `counterparty` = pointer del usuario, `receiveAmount` = `chargedAmount` y `expiresAt`. Envía un SMS `solicitud_pago` con la `approvalUrl`.
- [ ] Traducción de estados: `rejected` → `failed`/`customer_rejected`; `expired` → `expired`/`request_expired`; `cancelled` → `cancelled`/`request_cancelled`; `failed` → `failed`/`payment_failed`. Cada uno libera la reserva.
- [ ] Al completarse, suben el cupo y la comisión, baja el efectivo y se congela el recibo.
- [ ] Al leerla, el backend vence la operación pasada de `expiresAt` aunque el proxy no lo haya reportado, y cancela en el riel.
- [ ] `POST /operations/{operationId}/cancel` responde 200 antes de la aprobación. Después responde `409 operation_not_cancellable`, que también es la respuesta cuando el proxy devuelve `payment_not_cancellable`.
- [ ] `POST /operations/{operationId}/cash-handover` llena `cashHandedOverAt` y deja un evento de auditoría sin tocar saldos. Es idempotente. Sobre un retiro no completado responde `409 operation_not_completed`.
- [ ] El admin recibe 403 en crear, cancelar y confirmar la entrega.
- [ ] `GET /demo/payment-requests?phone=` lista los retiros pendientes de ese celular con su `approvalUrl`.
- [ ] Hay pruebas HTTP de cada estado final, cancelar antes y después de aprobar, el vencimiento con el reloj falso y la entrega.
