# API Relia: recursos de operaciones, saldos e historial del corresponsal

Type: grilling
Status: resolved
Blocked by:
Labels: wayfinder:grilling

## Question

¿Cómo se modelan en `relia-api.openapi.yaml` las operaciones del corresponsal y sus saldos? Hay que decidir:

- Un recurso único `/operations` con `type` (`deposit | withdrawal | quotaTopUp | cashAdjustment | commissionPayout`) o endpoints por tipo para crear y uno unificado para leer.
- Si el backend expone un endpoint de **cotización o vista previa** (desglose "Dile al usuario" / "Para ti", cupo y efectivo después, errores de cupo o efectivo) o si el frontend calcula el desglose con las reglas de §5.
- La validación de llave antes de confirmar (`GET /keys/{phone}` con el pointer enmascarado) y cómo se integra la invitación por WhatsApp.
- La forma del recurso operación: estado, motivo, montos (efectivo, billetera, comisión, modalidad), saldos antes y después (`quota`, `accruedCommission`, `cashOnHand`), `approvalUrl` y vencimiento cuando aplique.
- Cancelación (`POST /operations/{id}/cancel`) y confirmación del efectivo entregado en el retiro (¿es un evento del API o solo de la UI?).
- La carga de cupo: Bre-B (QR simulado) y Wallet (llave del corresponsal → su pointer).
- Login y saldos de la caja (`GET /agents/me`), historial con filtros y paginación, comprobante (¿recurso propio o la misma operación?) y "enviar comprobante por SMS".

Produce: la sección del corresponsal en `contracts/relia-api.openapi.yaml`.

## Answer

Resuelto en grilling (2 rondas, 2026-10-16), alineado al esquema [relia_schema_hackathon_v0.2.sql](../../../relia_schema_hackathon_v0.2.sql). Asset: [contracts/relia-api.openapi.yaml](../../../../contracts/relia-api.openapi.yaml) (Redocly: válido, 2 avisos cosméticos). [GLOSSARY.md](../../../../GLOSSARY.md) actualizado.

- **Modelo**: **Operación** = depósito, retiro o carga de cupo (`OP-000123`). El pago de comisión es una **Liquidación** (`settlement`, `LQ-000007`), una entidad aparte. El ajuste de efectivo es un **Movimiento** sin operación. El historial (`GET /agents/{agentId}/history`) une las tres cosas con `kind`, filtra por `kind` y `status`, pagina por cursor y ordena del más reciente al más antiguo.
- **Rutas**: se crea por tipo (`POST /agents/{agentId}/deposits | withdrawals | quota-top-ups | cash-adjustments`) y se lee con `GET /operations/{operationId}`. Las creaciones de depósito, retiro y carga responden `202` en `pending` y el frontend hace polling. Las rutas del corresponsal van bajo `/agents/{agentId}`, y el admin reutiliza las mismas rutas.
- **Vista previa**: `POST /agents/{agentId}/operations/preview` devuelve el desglose, los saldos después y `violations` con `shortfall`. Las reglas viven solo en el backend.
- **Llave**: `GET /keys/{phone}` responde 200 con el pointer enmascarado o 404 `key_not_found`. `POST /keys/{phone}/invitations` devuelve el enlace wa.me y el mensaje simulado.
- **Montos** (como en el esquema): `requestedAmount` (M), `roundAmount`, `fee { total, relia, agent }`, `netAmount` (lo que recibe el usuario) y `chargedAmount` (lo que paga el usuario).
- **Reserva**: se calcula desde las operaciones pendientes, sin columnas nuevas. Un depósito pendiente reserva `chargedAmount` de cupo y un retiro pendiente reserva `netAmount` de efectivo. Ya no se revisa el efectivo al aprobar. Los saldos exponen `quotaReserved` y `cashReserved`.
- **Confirmaciones**: crear un depósito exige `cashReceivedConfirmed: true`. En el retiro, `POST /operations/{operationId}/cash-handover` escribe un evento de auditoría (`cashHandedOverAt`) sin mover saldos.
- **Cancelar**: `POST /operations/{operationId}/cancel`, solo retiros y cargas pendientes antes de la aprobación; si no, 409 `operation_not_cancellable`. "Intentar de nuevo" crea una operación nueva con otro `Idempotency-Key`.
- **Carga de cupo**: `{ method: breB | wallet, requestedAmount }`, sin llave. Wallet cobra al `payment_pointer` del corresponsal, el mismo que recibe la liquidación.
- **Recibo**: es el recibo congelado del esquema, solo para depósito y retiro completados. `GET /operations/{operationId}/receipt?channel=` registra la emisión y devuelve `isCopy`. `POST …/receipt/send { channel: sms | whatsapp }`.
- **Saldos de entradas no completadas**: antes = después, con el último saldo conocido.
- **Ajuste de efectivo**: `{ mode: add | set, amount, reason? }`, con motivo por defecto. `422 no_change` si la variación es 0, e `insufficient_cash` si el total queda por debajo de lo reservado.
- **Sesión**: `POST /auth/login` devuelve `{ accessToken, role, agentId? }` y `GET /me` el perfil con sus saldos.
- **Fuera de alcance**: la consulta de recibos por el usuario (código + OTP).
