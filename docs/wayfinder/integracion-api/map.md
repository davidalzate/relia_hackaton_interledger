# Mapa: integración del frontend con la Relia API

Labels: wayfinder:map

## Destination

Cada endpoint de [relia-api.openapi.yaml](../../../contracts/relia-api.openapi.yaml) consumido por `frontend/relia` según [FLUJOS.md](../../../contracts/FLUJOS.md), sin mocks en `core/services/`, y el guion de demo (§12) corriendo de punta a punta contra el backend.

## Notes

- **Tracker:** markdown local, un archivo por ticket en [issues/](./issues/), con `Status: open|claimed|resolved` y `Blocked by:` (misma convención que [contratos-api](../contratos-api/map.md)).
- El frontend nunca llama al Rail Proxy.
- Empezar por [00](issues/00-base-http.md): interceptores, `Idempotency-Key`, `problem+json` y helper de polling los usan todos.

## Tickets por feature

Una feature = una rama y un PR. Orden sugerido de arriba abajo; dentro de cada feature, en el orden de la tabla.

Estados: `claimed` = PR abierto sin fusionar; `resolved` = fusionado.

### 0. Base y sesión

Hecho. PRs apilados #19 → #25.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [00](issues/00-base-http.md) | Base HTTP: cliente, auth, idempotencia, errores y polling | Todas | — | claimed · [#19](https://github.com/davidalzate/relia_hackaton_interledger/pull/19) |
| [01](issues/01-auth-login.md) | POST /auth/login | A1 Login | 00 | claimed · [#20](https://github.com/davidalzate/relia_hackaton_interledger/pull/20) |
| [02](issues/02-me.md) | GET /me | Arranque de la app | 01 | claimed · [#21](https://github.com/davidalzate/relia_hackaton_interledger/pull/21) |
| [03](issues/03-agent-detail.md) | GET /agents/{agentId} | B1 Caja, B7 Efectivo, B8 Historial (resumen), D2 Detalle | 00 | claimed · [#22](https://github.com/davidalzate/relia_hackaton_interledger/pull/22) |
| [04](issues/04-agent-history.md) | GET /agents/{agentId}/history | B1 (últimas 3), B8 Historial, D2 | 03 | claimed · [#24](https://github.com/davidalzate/relia_hackaton_interledger/pull/24) |
| [05](issues/05-cash-adjustments.md) | POST /agents/{agentId}/cash-adjustments | B7 Efectivo en caja | 03 | claimed · [#25](https://github.com/davidalzate/relia_hackaton_interledger/pull/25) |

### 1. Seguimiento de operaciones

Servicio listo en #27; depositar, retirar y cargar cupo lo conectan en sus pantallas.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [07](issues/07-operations-get.md) | GET /operations/{operationId} | B2.4, B3.2, B6, B9 | 00 | claimed · [#27](https://github.com/davidalzate/relia_hackaton_interledger/pull/27) |
| [08](issues/08-operations-cancel.md) | POST /operations/{operationId}/cancel | B3.2 Cancelar solicitud, B6 Cancelar | 07 | claimed · [#27](https://github.com/davidalzate/relia_hackaton_interledger/pull/27) |

### 2. Depositar (B2)

PR #28. Vista previa y llave las reusa Retirar.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [09](issues/09-operations-preview.md) | POST /agents/{agentId}/operations/preview | B2.1, B3.1 Desglose | 03 | claimed · [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28) |
| [10](issues/10-keys-get.md) | GET /keys/{phone} | B2.2, B3.1 Validar llave | 00 | claimed · [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28) |
| [11](issues/11-deposits.md) | POST /agents/{agentId}/deposits | B2.4 Enviando → B2.5 / B2.6 | 07, 10, 09 | claimed · [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28) |

### 3. Retirar (B3)

PR #29. En el demo se aprueba desde el enlace de C2; Mi billetera (26) lo reemplaza.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [12](issues/12-withdrawals.md) | POST /agents/{agentId}/withdrawals | B3.1 → B3.2 → B3.3 / B3.4 | 07, 10, 09 | claimed · [#29](https://github.com/davidalzate/relia_hackaton_interledger/pull/29) |
| [13](issues/13-cash-handover.md) | POST /operations/{operationId}/cash-handover | B3.3 → B4 "Sí, confirmo" | 12 | claimed · [#29](https://github.com/davidalzate/relia_hackaton_interledger/pull/29) |

### 4. Cargar cupo (B6)

PR #31. 23 es el botón del simulador Bre-B que completa la carga.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [06](issues/06-quota-top-ups.md) | POST /agents/{agentId}/quota-top-ups | B6 Cargar cupo | 07 | claimed · [#31](https://github.com/davidalzate/relia_hackaton_interledger/pull/31) |
| [23](issues/23-demo-breb-payment.md) | POST /demo/quota-top-ups/{operationId}/breb-payment | E1 Simulador Bre-B | 06 | claimed · [#31](https://github.com/davidalzate/relia_hackaton_interledger/pull/31) |

### 5. Llave inexistente y vinculación (B5, C1)

PR #32. Invitación y OTP simulados en el frontend (decisión de demo); la llave se crea con `POST /key-links` tras el OTP.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [14](issues/14-key-invitations.md) | POST /keys/{phone}/invitations | B5 Invitación WhatsApp | 10 | claimed · [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32) |
| [15](issues/15-key-links.md) | POST /key-links | C1.1 Vincular billetera (pública) | 00 | claimed · [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32) |
| [16](issues/16-key-links-verify.md) | POST /key-links/{challengeId}/verify | C1.2 Código OTP | 15 | claimed · [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32) |

### 6. Comprobante (B9)

Abre filas de B8 y D2.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [17](issues/17-receipt.md) | GET /operations/{operationId}/receipt | B9 Comprobante | 07 | open |
| [18](issues/18-receipt-send.md) | POST /operations/{operationId}/receipt/send | B9 Enviar SMS | 17 | open |

### 7. Admin (D1–D3)

20 desbloquea D2 (ids reales de corresponsales).

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [20](issues/20-agents-list.md) | GET /agents | D1 Inicio admin | 00 | open |
| [21](issues/21-settlements-list.md) | GET /settlements | D1 Pagos de comisión de hoy | 00 | open |
| [19](issues/19-settlements-get.md) | GET /settlements/{settlementId} | D3 Enviando, B8/D2 abrir fila | 00 | open |
| [22](issues/22-settlements-create.md) | POST /agents/{agentId}/settlements | D3 Confirmar pago | 19 | open |

### 8. Modo demo (E1, C2)

24 y 26 se necesitan antes para probar llaves y retiros.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [24](issues/24-demo-messages.md) | GET /demo/messages | C2 Teléfono: Mensajes, Ver SMS (OTP) | 00 | open |
| [25](issues/25-demo-messages-read.md) | POST /demo/messages/read | C2 al abrir el teléfono | 24 | open |
| [26](issues/26-demo-payment-requests.md) | GET /demo/payment-requests | C2 Mi billetera | 00 | open |
| [27](issues/27-demo-faults.md) | GET/PUT /demo/faults | E1 "El próximo pago falla" | 00 | open |
| [28](issues/28-demo-expire.md) | POST /demo/operations/{operationId}/expire | E1 "La solicitud vence ya" | 07 | open |
| [29](issues/29-demo-reset.md) | POST /demo/reset | E1 "Reiniciar demo" | 00 | open |

## Out of scope

- Paso 0 (permiso de Relia en el proxy): lo hace el operador, no el frontend.
- Cambios al contrato: si una integración lo necesita, se abre ticket en [contratos-api](../contratos-api/map.md).
