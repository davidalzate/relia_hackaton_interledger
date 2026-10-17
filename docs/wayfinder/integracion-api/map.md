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

Estados: `claimed` = PR abierto o fusionado en una rama intermedia (aún no está en `main`); `resolved` = en `main`.

**Estado al 2026-10-17:** 20 de 24 tickets en `main`. Faltan los 4 de Admin (19–22): #37 se fusionó en `feat/receipt-api` después de que esa rama entrara a `main` con #38, así que falta un PR de `feat/receipt-api` a `main`. Modo demo (24–29) quedó fuera de alcance.

### 0. Base y sesión

En `main` (#19 y #34).

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [00](issues/00-base-http.md) | Base HTTP: cliente, auth, idempotencia, errores y polling | Todas | — | resolved · [#19](https://github.com/davidalzate/relia_hackaton_interledger/pull/19) |
| [01](issues/01-auth-login.md) | POST /auth/login | A1 Login | 00 | resolved · [#20](https://github.com/davidalzate/relia_hackaton_interledger/pull/20) |
| [02](issues/02-me.md) | GET /me | Arranque de la app | 01 | resolved · [#21](https://github.com/davidalzate/relia_hackaton_interledger/pull/21) |
| [03](issues/03-agent-detail.md) | GET /agents/{agentId} | B1 Caja, B7 Efectivo, B8 Historial (resumen), D2 Detalle | 00 | resolved · [#22](https://github.com/davidalzate/relia_hackaton_interledger/pull/22) |
| [04](issues/04-agent-history.md) | GET /agents/{agentId}/history | B1 (últimas 3), B8 Historial, D2 | 03 | resolved · [#24](https://github.com/davidalzate/relia_hackaton_interledger/pull/24) |
| [05](issues/05-cash-adjustments.md) | POST /agents/{agentId}/cash-adjustments | B7 Efectivo en caja | 03 | resolved · [#25](https://github.com/davidalzate/relia_hackaton_interledger/pull/25) |

### 1. Seguimiento de operaciones

En `main` (#27 y #34). Depositar, Retirar y Cargar cupo lo usan.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [07](issues/07-operations-get.md) | GET /operations/{operationId} | B2.4, B3.2, B6, B9 | 00 | resolved · [#27](https://github.com/davidalzate/relia_hackaton_interledger/pull/27) |
| [08](issues/08-operations-cancel.md) | POST /operations/{operationId}/cancel | B3.2 Cancelar solicitud, B6 Cancelar | 07 | resolved · [#27](https://github.com/davidalzate/relia_hackaton_interledger/pull/27) |

### 2. Depositar (B2)

En `main` (#28 y #34). Vista previa y llave las reusa Retirar.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [09](issues/09-operations-preview.md) | POST /agents/{agentId}/operations/preview | B2.1, B3.1 Desglose | 03 | resolved · [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28) |
| [10](issues/10-keys-get.md) | GET /keys/{phone} | B2.2, B3.1 Validar llave | 00 | resolved · [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28) |
| [11](issues/11-deposits.md) | POST /agents/{agentId}/deposits | B2.4 Enviando → B2.5 / B2.6 | 07, 10, 09 | resolved · [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28) |

### 3. Retirar (B3)

En `main` (#29). El usuario escanea un QR con la `approvalUrl` y aprueba en su billetera; nada se envía a C2.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [12](issues/12-withdrawals.md) | POST /agents/{agentId}/withdrawals | B3.1 → B3.2 → B3.3 / B3.4 | 07, 10, 09 | resolved · [#29](https://github.com/davidalzate/relia_hackaton_interledger/pull/29) |
| [13](issues/13-cash-handover.md) | POST /operations/{operationId}/cash-handover | B3.3 → B4 "Sí, confirmo" | 12 | resolved · [#29](https://github.com/davidalzate/relia_hackaton_interledger/pull/29) |

### 4. Cargar cupo (B6)

En `main` (#31 y #38). 23 es el botón del simulador Bre-B que completa la carga.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [06](issues/06-quota-top-ups.md) | POST /agents/{agentId}/quota-top-ups | B6 Cargar cupo | 07 | resolved · [#31](https://github.com/davidalzate/relia_hackaton_interledger/pull/31) |
| [23](issues/23-demo-breb-payment.md) | POST /demo/quota-top-ups/{operationId}/breb-payment | E1 Simulador Bre-B | 06 | resolved · [#31](https://github.com/davidalzate/relia_hackaton_interledger/pull/31) |

### 5. Llave inexistente y vinculación (B5, C1)

En `main` (#32 y #38). Invitación y OTP simulados en el frontend (decisión de demo); la llave se crea con `POST /key-links` tras el OTP.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [14](issues/14-key-invitations.md) | POST /keys/{phone}/invitations | B5 Invitación WhatsApp | 10 | resolved · [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32) |
| [15](issues/15-key-links.md) | POST /key-links | C1.1 Vincular billetera (pública) | 00 | resolved · [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32) |
| [16](issues/16-key-links-verify.md) | POST /key-links/{challengeId}/verify | C1.2 Código OTP | 15 | resolved · [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32) |

### 6. Comprobante (B9)

En `main` (#33 y #38). Abre filas de B8 y D2.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [17](issues/17-receipt.md) | GET /operations/{operationId}/receipt | B9 Comprobante | 07 | resolved · [#33](https://github.com/davidalzate/relia_hackaton_interledger/pull/33) |
| [18](issues/18-receipt-send.md) | POST /operations/{operationId}/receipt/send | B9 Enviar SMS | 17 | resolved · [#33](https://github.com/davidalzate/relia_hackaton_interledger/pull/33) |

### 7. Admin (D1–D3)

**No está en `main`.** #37 se fusionó en `feat/receipt-api`; falta llevar esa rama a `main`. D2 y D3 construidas contra el API.

| # | Endpoint | Pantalla | Bloqueado por | Estado |
|---|---|---|---|---|
| [20](issues/20-agents-list.md) | GET /agents | D1 Inicio admin | 00 | claimed · [#37](https://github.com/davidalzate/relia_hackaton_interledger/pull/37) |
| [21](issues/21-settlements-list.md) | GET /settlements | D1 Pagos de comisión de hoy | 00 | claimed · [#37](https://github.com/davidalzate/relia_hackaton_interledger/pull/37) |
| [19](issues/19-settlements-get.md) | GET /settlements/{settlementId} | D3 Enviando, B8/D2 abrir fila | 00 | claimed · [#37](https://github.com/davidalzate/relia_hackaton_interledger/pull/37) |
| [22](issues/22-settlements-create.md) | POST /agents/{agentId}/settlements | D3 Confirmar pago | 19 | claimed · [#37](https://github.com/davidalzate/relia_hackaton_interledger/pull/37) |

## Out of scope

- **Modo demo (E1, C2)**: se quitó de las tareas (decisión del 2026-10-17). No se integran `GET /demo/messages`, `POST /demo/messages/read`, `GET /demo/payment-requests`, `GET/PUT /demo/faults`, `POST /demo/operations/{id}/expire` ni `POST /demo/reset`; el teléfono simulado (C2) sigue con los SMS e invitaciones del frontend. El simulador Bre-B (23) sí está integrado en Cargar cupo.

- Paso 0 (permiso de Relia en el proxy): lo hace el operador, no el frontend.
- Cambios al contrato: si una integración lo necesita, se abre ticket en [contratos-api](../contratos-api/map.md).
