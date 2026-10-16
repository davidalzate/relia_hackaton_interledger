# Flujos y endpoints

Qué endpoints llamar, y en qué orden, para cada flujo de Relia. Cada paso dice la pantalla ([detalle_pantallas.md](../docs/detalle_pantallas.md)), la llamada a la **Relia API** (frontend → backend) y, cuando aplica, lo que el backend hace en el **Rail Proxy** (backend → proxy). El frontend nunca llama al proxy.

Cómo montar Postman: [README.md](README.md). Fuente de verdad: los YAML de esta carpeta.

## Índice

- [Reglas comunes](#reglas-comunes)
- [0. Preparación del riel (operador, una vez)](#0-preparación-del-riel-operador-una-vez)
- [1. Login y sesión](#1-login-y-sesión)
- [2. Caja (inicio del corresponsal)](#2-caja-inicio-del-corresponsal)
- [3. Efectivo en caja](#3-efectivo-en-caja)
- [4. Cargar cupo](#4-cargar-cupo)
- [5. Depositar](#5-depositar)
- [6. Retirar](#6-retirar)
- [7. Llave inexistente: invitación por WhatsApp](#7-llave-inexistente-invitación-por-whatsapp)
- [8. Vincular billetera (página pública)](#8-vincular-billetera-página-pública)
- [9. Historial y comprobante](#9-historial-y-comprobante)
- [10. Admin: panel, detalle y pago de comisión](#10-admin-panel-detalle-y-pago-de-comisión)
- [11. Modo demo](#11-modo-demo)
- [12. Guion de demo completo](#12-guion-de-demo-completo)
- [Anexo: estados y errores](#anexo-estados-y-errores)
- [Diferencias con el prototipo](#diferencias-con-el-prototipo)

---

## Reglas comunes

- **Auth:** todo lleva `Authorization: Bearer <token>` salvo `POST /auth/login`, `/key-links*` y `/demo/*`.
- **Creaciones:** llevan `Idempotency-Key`. Si la respuesta fue 4xx o 503 no se creó nada: se reintenta con **la misma** clave. "Intentar de nuevo" tras una operación `failed`, `expired` o `cancelled` usa una clave **nueva**.
- **Polling:** las operaciones se crean en `pending` (202). Se consulta `GET /operations/{operationId}` cada 1–2 s hasta `completed`, `failed`, `expired` o `cancelled`. Las liquidaciones se consultan igual con `GET /settlements/{settlementId}`.
- **Dinero:** en la API, enteros en COP. El backend lo pasa al proxy como `{ value: pesos × 100, assetCode: "COP", assetScale: 2 }`.
- **Errores:** `application/problem+json` con `code`. El frontend traduce el `code` al texto de la pantalla (ver [Anexo](#anexo-estados-y-errores)).

---

## 0. Preparación del riel (operador, una vez)

Sin esto, depósitos y pagos de comisión responden **503**. Se hace en el proxy, no desde el frontend.

| Paso | Rail Proxy |
|---|---|
| Ver estado | `GET /health` → `reliaGrant` debe ser `active` |
| Pedir permiso | `POST /relia-grant` con `{ debitLimit, interval }` → 202 con `approvalUrl` |
| Aprobar | El operador abre `approvalUrl` en la wallet de prueba de Relia y aprueba |
| Confirmar | `GET /relia-grant` → `status: active` |

---

## 1. Login y sesión

Pantalla: **A1 Login**.

| Paso | Relia API | Notas |
|---|---|---|
| Entrar | `POST /auth/login` `{ email, password }` | Devuelve `accessToken`, `role` y `agentId` (solo `agent`). `401 invalid_credentials` → "Correo o contraseña incorrectos." |
| Decidir destino | — | `role: agent` → Caja (flujo 2). `role: admin` → Inicio admin (flujo 10). |
| Recargar la app | `GET /me` | Perfil; si es `agent`, trae el corresponsal con saldos. |
| Cerrar sesión | — | Se descarta el token en el cliente. No hay endpoint. |

---

## 2. Caja (inicio del corresponsal)

Pantalla: **B1 Caja**.

| Dato en pantalla | Relia API |
|---|---|
| Cupo, comisión acumulada, efectivo en caja, avisos de cupo/efectivo bajo | `GET /agents/{agentId}` → `balances` |
| Últimas 3 operaciones | `GET /agents/{agentId}/history?limit=3` |

Los avisos de "bajo" (< $50.000) los calcula el frontend con `balances.quota` y `balances.cashOnHand`.

---

## 3. Efectivo en caja

Pantalla: **B7 Efectivo en caja**.

| Paso | Relia API | Notas |
|---|---|---|
| Ver efectivo actual | `GET /agents/{agentId}` | `balances.cashOnHand` y `cashReserved` |
| Guardar | `POST /agents/{agentId}/cash-adjustments` + `Idempotency-Key` | **Sumar:** `{ mode: "add", amount }`. **Fijar total:** `{ mode: "set", amount }`. Responde 201 (síncrono, sin polling). |
| Mostrar "antes → después" | — | Viene en la respuesta: `cashOnHand.before` / `after` |

Errores: `422 insufficient_cash` (fijar por debajo de lo reservado), `422 no_change`. No toca el proxy.

---

## 4. Cargar cupo

Pantalla: **B6 Cargar cupo**. Dos métodos.

### 4a. Bre-B (simulado, no pasa por el proxy)

| Paso | Relia API |
|---|---|
| Generar QR | `POST /agents/{agentId}/quota-top-ups` + `Idempotency-Key`, body `{ method: "breB", requestedAmount }` → 202 con `qrPayload` y `expiresAt` (10 min) |
| Esperar pago | `GET /operations/{operationId}` cada 1–2 s |
| Simular el pago (panel Modo demo) | `POST /demo/quota-top-ups/{operationId}/breb-payment` → la operación pasa a `completed` |
| Cancelar | `POST /operations/{operationId}/cancel` |

### 4b. Wallet (pago real en la red de prueba)

| Paso | Relia API | Rail Proxy (lo hace el backend) |
|---|---|---|
| Enviar solicitud | `POST /agents/{agentId}/quota-top-ups` + `Idempotency-Key`, body `{ method: "wallet", requestedAmount }` → 202 con `approvalUrl` y `expiresAt` (5 min) | `POST /payments` `direction: incoming`, `counterparty` = billetera del corresponsal |
| Aprobar | El corresponsal abre `approvalUrl` y aprueba **en su wallet de prueba** (en demo, desde "Mi billetera": `GET /demo/payment-requests?phone=<celular del corresponsal>`) | El proxy detecta la aprobación consultando el riel |
| Esperar | `GET /operations/{operationId}` cada 1–2 s | El backend consulta `GET /payments/{paymentId}` |
| Cancelar | `POST /operations/{operationId}/cancel` (solo antes de aprobar; si no, `409 operation_not_cancellable`) | `POST /payments/{paymentId}/cancel` |

Final: `completed` → "Cupo cargado" con `balances.quota.before/after`. Fallos → B6.5 según `failureCode`.

---

## 5. Depositar

Pantalla: **B2 Depositar**. El usuario entrega efectivo; Relia paga a su billetera.

| Paso | Pantalla | Relia API | Rail Proxy (backend) |
|---|---|---|---|
| 1. Desglose en vivo | B2.1, al escribir monto o cambiar "valor redondo" | `POST /agents/{agentId}/operations/preview` `{ type: "deposit", requestedAmount, roundAmount }` | — |
| 2. Validar llave | B2.2, botón "Validar llave" | `GET /keys/{phone}` → 200 con `walletAddressMasked` | — |
| 2b. Llave no existe | `404 key_not_found` | → [flujo 7](#7-llave-inexistente-invitación-por-whatsapp) | — |
| 3. Confirmar efectivo | B4 "Sí, confirmo" | — (solo UI) | — |
| 4. Crear depósito | B2.4 Enviando | `POST /agents/{agentId}/deposits` + `Idempotency-Key`, body `{ phone, requestedAmount, roundAmount, cashReceivedConfirmed: true }` → 202 | `POST /payments` `direction: outgoing`, `counterparty` = billetera del usuario, `receiveAmount` = `netAmount` |
| 5. Esperar | B2.4 | `GET /operations/{operationId}` cada 1–2 s | `GET /payments/{paymentId}` |
| 6. Completado | B2.5 | Datos en la operación: `netAmount`, `fee`, `balances` | — |
| 7. Comprobante | B9 | → [flujo 9](#9-historial-y-comprobante) | — |

- `preview` siempre responde 200; si `violations` no está vacío (`amount_too_low`, `insufficient_quota` con `shortfall`) se deshabilita el botón.
- Errores al crear: `422 insufficient_quota` → "Cupo insuficiente"; `503 rail_unavailable` → "El simulador no respondió. No se movió dinero."
- `failed` con `payment_failed` → B2.6.

---

## 6. Retirar

Pantalla: **B3 Retirar**. El usuario paga desde su billetera y recibe efectivo.

| Paso | Pantalla | Relia API | Rail Proxy (backend) |
|---|---|---|---|
| 1. Desglose | B3.1 | `POST /agents/{agentId}/operations/preview` `{ type: "withdrawal", requestedAmount, roundAmount }` | — |
| 2. Validar llave | B3.1 | `GET /keys/{phone}` | — |
| 3. Enviar solicitud | B3.1 "Enviar solicitud de pago" | `POST /agents/{agentId}/withdrawals` + `Idempotency-Key`, body `{ phone, requestedAmount, roundAmount }` → 202 con `approvalUrl`, `expiresAt` (5 min) | `POST /payments` `direction: incoming`, `counterparty` = billetera del usuario, `receiveAmount` = `chargedAmount` |
| 4. Esperar aprobación | B3.2 (contador desde `expiresAt`, "No entregues efectivo todavía") | `GET /operations/{operationId}` cada 1–2 s | `GET /payments/{paymentId}` |
| 4b. El usuario aprueba | C2 "Mi billetera" | `GET /demo/payment-requests?phone=<celular del usuario>` → abre `approvalUrl` y aprueba **en la wallet de prueba** | El proxy detecta la aprobación |
| 4c. Cancelar | B3.2 "Cancelar solicitud" | `POST /operations/{operationId}/cancel` | `POST /payments/{paymentId}/cancel` |
| 5. Pago aprobado | B3.3 "Entrega en efectivo" | Operación `completed` | — |
| 6. Confirmar entrega | B4 "Sí, confirmo" | `POST /operations/{operationId}/cash-handover` (no cambia saldos; queda en auditoría) | — |
| 7. Comprobante | B9 | → [flujo 9](#9-historial-y-comprobante) | — |

Errores (B3.4), por `failureCode` de la operación:

| `status` / `failureCode` | Texto |
|---|---|
| `failed` / `customer_rejected` | "El usuario rechazó el pago" |
| `expired` / `request_expired` | "La solicitud venció" |
| `cancelled` / `request_cancelled` | "Solicitud cancelada" |
| `failed` / `payment_failed` | "El pago falló" |
| Al crear: `422 insufficient_cash` | "Efectivo insuficiente" |

---

## 7. Llave inexistente: invitación por WhatsApp

Pantalla: **B5** (desde depositar o retirar cuando `GET /keys/{phone}` da `404 key_not_found`).

| Paso | Relia API |
|---|---|
| Invitar | `POST /keys/{phone}/invitations` + `Idempotency-Key` → 201 con `waLink` y `message` |
| Ver el WhatsApp en el teléfono simulado | `GET /demo/messages?phone={phone}` → mensaje `invitacion_wallet` con `link` a la página pública |
| El usuario vincula | → [flujo 8](#8-vincular-billetera-página-pública) |
| Volver y validar otra vez | `GET /keys/{phone}` → ahora 200 |

---

## 8. Vincular billetera (página pública)

Pantalla: **C1 Vincular billetera**. Sin login.

| Paso | Pantalla | Relia API | Rail Proxy (backend) |
|---|---|---|---|
| 1. Enviar código | C1.1 | `POST /key-links` `{ phone, walletAddress }` → 201 con `challengeId`, `expiresAt` (5 min), `maxAttempts` | `GET /wallet-addresses?pointer=...` (valida que exista) |
| 2. Leer el OTP | C2 "Ver SMS" | `GET /demo/messages?phone={phone}` → mensaje `otp` | — |
| 3. Verificar | C1.2 | `POST /key-links/{challengeId}/verify` `{ code }` → 200 con `walletAddress` y `linkedAt` | — |
| Reenviar código | C1.2 | `POST /key-links` otra vez (invalida el desafío anterior) | — |

Errores:

| Código | Cuándo |
|---|---|
| `409 key_already_linked` | El celular ya tiene llave (la llave es permanente) |
| `409 wallet_already_linked` | La billetera ya está vinculada a otro celular |
| `409 phone_reserved` | Celular o billetera de un corresponsal o de Relia |
| `422 wallet_not_found` | El payment pointer no existe en el riel |
| `422 invalid_otp` (`attemptsRemaining`), `otp_expired`, `otp_attempts_exceeded` | Al verificar |
| `429 rate_limited` | Demasiados intentos; esperar `Retry-After` |

---

## 9. Historial y comprobante

Pantallas: **B8 Historial**, **B9 Comprobante**.

| Paso | Relia API | Notas |
|---|---|---|
| Listar | `GET /agents/{agentId}/history` | Paginado con `cursor` / `nextCursor`. Resumen de saldos: `GET /agents/{agentId}` |
| Filtrar | `?kind=deposit\|withdrawal\|quotaTopUp\|cashAdjustment\|settlement` | El filtro "Efectivo" es `cashAdjustment`; "Pago de comisión" es `settlement` |
| Abrir una fila | Si tiene `operationId`: `GET /operations/{operationId}`. Si tiene `settlementId`: `GET /settlements/{settlementId}` | Los ajustes de efectivo no tienen detalle aparte; todo viene en la fila |
| Recibo | `GET /operations/{operationId}/receipt?channel=screen` | Solo depósitos y retiros `completed`. Si no, `409 receipt_not_available` u `operation_not_completed`. Desde la segunda llamada `isCopy: true` ("COPIA") |
| Imprimir | `GET /operations/{operationId}/receipt?channel=print` | Registra la emisión |
| SMS al usuario | `POST /operations/{operationId}/receipt/send` `{ channel: "sms" }` → 202 | Queda en `GET /demo/messages` |

Para cargas, ajustes y liquidaciones el frontend arma el comprobante con los datos de la operación, la fila del historial o la liquidación.

---

## 10. Admin: panel, detalle y pago de comisión

Pantallas: **D1, D2, D3**. Rol `admin`.

| Paso | Pantalla | Relia API | Rail Proxy (backend) |
|---|---|---|---|
| Indicadores y tabla de corresponsales | D1 | `GET /agents` → `items` (filas) y `totals` (tarjetas) en una llamada | — |
| "Pagos de comisión" de hoy | D1 | `GET /settlements?from=<hoy en America/Bogota>` | — |
| Detalle de corresponsal | D2 | `GET /agents/{agentId}` + `GET /agents/{agentId}/history` | — |
| Comprobante de una fila | D2 → B9 | Igual que el [flujo 9](#9-historial-y-comprobante) | — |
| Pagar comisión | D3 "Confirmar pago" | `POST /agents/{agentId}/settlements` + `Idempotency-Key` → 202 `pending` | `POST /payments` `direction: outgoing`, `counterparty` = billetera del corresponsal (salvo `walletSimulated`, que se paga sin riel) |
| Esperar | D3 "Enviando" | `GET /settlements/{settlementId}` hasta `paid` o `voided` | `GET /payments/{paymentId}` |

Errores: `409 settlement_in_progress`, `422 no_commission` (→ "Sin comisión"), `503 rail_unavailable`. `voided` con `voidReason` → "El pago falló. No se movió dinero."

El admin **no** opera por el corresponsal: depósitos, retiros, cargas, ajustes, cancelar y confirmar entrega le responden `403 forbidden`.

---

## 11. Modo demo

Panel **E1** y teléfono **C2**. Sin login; solo con `DEMO_MODE=true` (si no, 404).

| Botón | Relia API |
|---|---|
| Simulador Bre-B: "Simular pago recibido" | `POST /demo/quota-top-ups/{operationId}/breb-payment` |
| "Abrir teléfono del usuario": Mensajes | `GET /demo/messages?phone=...` (sin `phone`, todos). Contador: `unreadCount` |
| Al abrir el teléfono | `POST /demo/messages/read` `{ phone }` (sin `phone`, marca todos) |
| "Mi billetera" (solicitudes pendientes) | `GET /demo/payment-requests?phone=...` → cada una trae `approvalUrl` para aprobar en la wallet real |
| "El próximo pago falla" | `PUT /demo/faults` `{ failNextPayment: true }`. Ver estado: `GET /demo/faults` |
| "La solicitud vence ya" | `POST /demo/operations/{operationId}/expire` (retiros y cargas pendientes) |
| "Inscribir celular (página pública)" | Navega a C1 → [flujo 8](#8-vincular-billetera-página-pública) |
| "Reiniciar demo" | `POST /demo/reset` → 204 |

`POST /demo/reset` deja 4 corresponsales de ejemplo (Rosa en ceros), el **3000000001** vinculado a `$ilp.interledger-test.dev/jeffdev` y el **3000000002** sin llave. No devuelve dinero ya movido en las billeteras reales.

---

## 12. Guion de demo completo

Orden del guion de [prototype.md §12](../docs/prototype.md), con sus llamadas. Antes: [paso 0](#0-preparación-del-riel-operador-una-vez) y `POST /demo/reset`.

| # | Paso del guion | Llamadas |
|---|---|---|
| 1 | Entrar como Rosa, ver avisos | `POST /auth/login` → `GET /agents/{agentId}` → `GET /agents/{agentId}/history?limit=3` |
| 2 | Asignar efectivo $300.000 | `POST /agents/{agentId}/cash-adjustments` `{ mode: "add", amount: 300000 }` |
| 3 | Cargar cupo por Bre-B y simular el pago | `POST /agents/{agentId}/quota-top-ups` `{ method: "breB", requestedAmount }` → `POST /demo/quota-top-ups/{operationId}/breb-payment` → `GET /operations/{operationId}` |
| 4 | Depositar $50.000 al 3000000001 | `POST .../operations/preview` (varias veces) → `GET /keys/3000000001` → `POST .../deposits` → `GET /operations/{id}` hasta `completed` → `GET /operations/{id}/receipt` |
| 5 | Retirar $100.000 | `POST .../operations/preview` → `GET /keys/3000000001` → `POST .../withdrawals` → `GET /demo/payment-requests?phone=3000000001` → aprobar en la wallet → `GET /operations/{id}` hasta `completed` → `POST /operations/{id}/cash-handover` |
| 6 | Depositar a un celular sin llave e invitarlo | `GET /keys/3000000002` (404) → `POST /keys/3000000002/invitations` → `GET /demo/messages?phone=3000000002` → `POST /key-links` → `GET /demo/messages?phone=3000000002` (OTP) → `POST /key-links/{challengeId}/verify` → `GET /keys/3000000002` → seguir el flujo 5 |
| 7 | Historial y comprobante | `GET /agents/{agentId}/history` → `GET /operations/{id}/receipt` |
| 8 | Admin: totales, detalle de Rosa y pago de comisión | `POST /auth/login` (admin) → `GET /agents` + `GET /settlements?from=hoy` → `GET /agents/{rosaId}` + `/history` → `POST /agents/{rosaId}/settlements` → `GET /settlements/{id}` hasta `paid` |

Paso 6: hace falta una billetera COP libre para el 3000000002 (ticket [11](../docs/wayfinder/contratos-api/issues/11-billetera-para-el-celular-sin-llave.md), abierto). `jeffdev` ya está tomada y las de Relia y Rosa dan `409 phone_reserved`.

---

## Anexo: estados y errores

### Estado del pago en el proxy → estado de la operación

| Pago de riel (proxy) | Operación (API) | `failureCode` |
|---|---|---|
| `pending_approval`, `processing` | `pending` | null |
| `completed` | `completed` | null |
| `rejected` | `failed` | `customer_rejected` |
| `expired` | `expired` | `request_expired` |
| `cancelled` | `cancelled` | `request_cancelled` |
| `failed` | `failed` | `payment_failed` |

En liquidaciones: `completed` → `paid`; `failed` → `voided`.

### Quién llama al proxy

| Flujo | Llamada al proxy |
|---|---|
| Vincular llave | `GET /wallet-addresses?pointer=` |
| Depósito | `POST /payments` outgoing → usuario |
| Retiro | `POST /payments` incoming ← usuario |
| Carga por Wallet | `POST /payments` incoming ← corresponsal |
| Carga por Bre-B | Ninguna (simulada en el backend) |
| Pago de comisión | `POST /payments` outgoing → corresponsal |
| Cancelar, "vencer ya", reiniciar demo | `POST /payments/{paymentId}/cancel` |
| Seguimiento de cualquier pago | `GET /payments/{paymentId}` |
| "El próximo pago falla" | Ninguna: el backend marca `failed` sin llamar al riel |

---

## Diferencias con el prototipo

El prototipo (`Relia.dc.html`) simula cosas que en el contrato son reales o no existen:

- **Aprobar o rechazar** un retiro o una carga por Wallet no tiene endpoint: se hace en la wallet de prueba abriendo `approvalUrl`. "Mi billetera" y el panel solo listan las solicitudes y enlazan a esa URL.
- **"Usuario rechaza"** (interruptor permanente) no existe. Para probar un rechazo, se rechaza en la wallet de prueba.
- **"Simular que la llave no existe"** no existe. Se usa un celular sin llave real (el 3000000002 tras el reinicio).
- **Usuario de ejemplo:** el prototipo dice `usuario-demo`; tras `POST /demo/reset` el 3000000001 queda vinculado a `jeffdev`.
- **Reemplazar la billetera** de un celular ya vinculado no está permitido: `409 key_already_linked`.
