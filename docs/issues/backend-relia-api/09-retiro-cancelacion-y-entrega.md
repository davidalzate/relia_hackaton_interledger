# 09: Retiro, cancelación y entrega de efectivo

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal le pide un pago a la billetera del usuario. El usuario aprueba en su wallet de prueba y, solo entonces, el corresponsal entrega el efectivo y confirma la entrega. Quien presenta ve la solicitud en "Mi billetera" del teléfono simulado. `IRailClient` gana cancelar pago.

**Blocked by:** 07 (Depósito de punta a punta)

**Status:** ready-for-human (implementado; probado contra un proxy falso y contra el proxy real (crear y cancelar). Falta que una persona apruebe y rechace en la red de prueba, y correr la colección)

- [x] `POST /agents/{agentId}/withdrawals` (idempotente) responde `202` con `approvalUrl` y `expiresAt` (+5 min). Reserva el neto del efectivo. Si no alcanza, responde `422 insufficient_cash` con `shortfall`.
- [x] Llama `POST /payments` incoming con `counterparty` = pointer del usuario, `receiveAmount` = monto cobrado, `expiresAt` y una `description` legible.
- [x] Envía un SMS de solicitud de pago con la `approvalUrl`.
- [x] Traducción de estados: `rejected` → `failed`/`customer_rejected`; `expired` → `expired`/`request_expired`; `cancelled` → `cancelled`/`request_cancelled`; `failed` → `failed`/`payment_failed`. Cada uno libera la reserva y deja `failure_code`.
- [x] Al completarse: el cupo sube el neto, el efectivo baja el neto, la comisión sube la parte del corresponsal y el recibo queda congelado.
- [x] `POST /operations/{operationId}/cancel` responde `200` antes de la aprobación y cancela en el riel. Después, o si el proxy devuelve `payment_not_cancellable`, responde `409 operation_not_cancellable`.
- [x] `POST /operations/{operationId}/cash-handover` llena `cashHandedOverAt` y deja un evento de auditoría sin tocar saldos. Repetirlo es idempotente. Sobre un retiro no completado responde `409 operation_not_completed`.
- [x] El admin recibe `403` al crear, cancelar y confirmar la entrega.
- [x] `GET /demo/payment-requests?phone=` lista las solicitudes pendientes de ese celular con `approvalUrl`, `agentName` y `chargedAmount`.
- [ ] Verificado contra la red de prueba con una persona aprobando: retiro de $100.000 aprobado, otro rechazado, otro cancelado antes de aprobar, cancelar después de aprobar (409) y la entrega confirmada dos veces. `v_agent_balance_check` queda en 0.

## Notas de implementación

- **Creación**: mismo esquema que el depósito, dentro de la transacción de `[Idempotent]`.
  - Bloquea los saldos, aplica la regla `amount_too_low` y calcula el efectivo disponible (efectivo − reservado). Si no alcanza → `insufficient_cash` con `shortfall`.
  - El pago es `incoming` de la billetera del usuario a la de Relia por el **monto cobrado**, con `expiresAt` = ahora + 5 min, redondeado hacia arriba al minuto (queda entre 5 y 6 min), y la descripción "Retiro de $ 100.000 en Droguería San José".
  - Con la respuesta del proxy se aplica el sincronizador. Si la operación sigue pendiente y hay `approvalUrl`, se envía el SMS `payment_request` (`solicitud_pago`) con el enlace en `link` y el `operationId`.
  - `AddCashOperation` y `RailDescription` ahora son compartidos con el depósito.
- **`payments.id`** es determinista por (corresponsal, `withdrawal`, clave), como en el depósito.
  - El proxy incluye `expiresAt` en su huella. Por eso el plazo se redondea al minuto: un reintento después de un timeout dentro del mismo minuto manda el mismo cuerpo y recupera el mismo pago.
  - Si el reintento cruza el minuto, responde `409 idempotency_conflict`. No hay doble cobro: el pago huérfano del proxy nunca llegó al usuario (su SMS se deshizo con el rollback) y vence solo.
- **Traducción y completado**: los da `IOperationSync` (ticket 07). El retiro completado suma el neto al cupo, resta el neto del efectivo, suma la parte del corresponsal a la comisión y congela el recibo.
  - `approval_url` del pago se vacía cuando el riel deja `pending_approval`, para que la operación y "Mi billetera" dejen de ofrecer aprobar. La URL vieja queda en `payment_events`.
- **Cancelar** (`IRailClient.CancelPaymentAsync`):
  - un depósito, o una operación no pendiente, responde `409 operation_not_cancellable` sin llamar al riel;
  - con pago de riel, la cancelación se pide fuera de la transacción. Riel caído, o cualquier otro error del proxy → `503`. `payment_not_cancellable` → se refresca la operación y responde `409`;
  - la lectura se aplica con la operación bloqueada y el evento de origen `agent`. Si el resultado no es `cancelled` (por ejemplo, el riel ya completó), lo aplicado queda guardado y responde `409`;
  - sin pago de riel, solo un pago Bre-B simulado (ticket 10) se cancela localmente, con `IOperationSync.CloseWithoutMovementAsync`. Un pago de Open Payments sin id del proxy es una inconsistencia y responde `500`.
- **Entrega del efectivo**: solo retiros `completed`; otro tipo o estado → `409 operation_not_completed`.
  - Con la fila bloqueada, llena `cash_handed_over_at` y escribe un `operation_event` `completed→completed` (agent, "Efectivo entregado al usuario").
  - Si ya estaba, devuelve la operación sin escribir.
- **Corresponsal ajeno o admin**: el admin recibe `403` por rol en el controlador; un corresponsal ajeno, por `EnsureCanOperateAgent`.
- **`GET /demo/payment-requests?phone=`** (`[DemoOnly]`): retiros cuyo usuario es ese celular, y cargas Wallet cuyo corresponsal es ese celular, en `pending` con `approval_url` y un `expires_at` futuro en su último pago. El más reciente primero.
- **Vencer en el backend** pasado `expiresAt`, sin esperar al proxy, es del ticket 11.
- **Verificado**:
  - **Contra un proxy falso** (scratchpad):
    - $100.000 redondo → `202` con `approvalUrl` y `expiresAt` +5 min, y `cashReserved` 100.000. La misma clave da la misma operación;
    - el proxy recibió `incoming` desde `$…/jeffdev` por `10100000` (COP, escala 2) con `expiresAt` y la descripción legible;
    - llegaron el SMS `solicitud_pago` con enlace y la solicitud en "Mi billetera" (sin `phone` → `422`);
    - entregar antes de completar → `409 operation_not_completed`;
    - `processing` y luego `completed` → cupo +100.000, efectivo −100.000, comisión +500, recibo `withdrawal`;
    - cancelar el completado → `409`;
    - entregar dos veces → mismo `cashHandedOverAt` y un solo evento;
    - el admin crea, cancela o entrega → `403`;
    - `rejected` → `failed`/`customer_rejected`; `expired` → `expired`/`request_expired`; `failed` → `failed`/`payment_failed`;
    - cancelar antes de aprobar → `200` `cancelled`/`request_cancelled`, reserva liberada, cancelado en el proxy; cancelar otra vez → `409`;
    - cancelar con el riel en `processing` → `409` `payment_not_cancellable`, la operación sigue y luego se completa;
    - sin efectivo → `422 insufficient_cash`; sin llave → `404`; cancelar o entregar un depósito → `409`;
    - "Mi billetera" queda vacía al final; el historial muestra cada estado con su `failureCode`;
    - `v_agent_balance_check` en 0.
  - **Después de la revisión**:
    - el proxy crea el pago pero responde tarde (timeout de 1 s) → `503`; el reintento con la misma clave → `202` OP-000034, con un solo pago en el proxy;
    - una solicitud con el plazo vencido (forzado por SQL) desaparece de "Mi billetera";
    - el guion completo se repitió sin cambios y los descuadres siguen en 0.
  - **Contra el proxy real**: retiro de $10.000 → `202` con un `approvalUrl` real de `auth.interledger-test.dev`. Cancelar → `200` `cancelled`/`request_cancelled`. La bitácora del pago queda `create:pending_approval v1 | refresh:… (descartada) | cancel:cancelled v2`.
  - **Pendiente**: la aprobación y el rechazo con una persona en la wallet de prueba (`$…/jeffdev` necesita saldo).

  Quedaron en la base local de Luis, contra el proxy falso, los retiros OP-000025 a OP-000031 y OP-000033 a OP-000041 (sin dinero real), y el OP-000032 real cancelado.

