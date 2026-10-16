# API Relia: panel admin y pago de comisión

Type: grilling
Status: resolved
Blocked by: 04
Labels: wayfinder:grilling

## Question

¿Qué endpoints con rol `admin` necesita el panel (pantallas D1–D3) en `relia-api.openapi.yaml`?

- Indicadores: total movido, depósitos, retiros, comisiones por pagar y pagadas hoy. ¿Un endpoint agregado o se calculan en el frontend?
- Lista de corresponsales con métricas (el esquema ya tiene la vista `v_resumen_admin_corresponsal`). El detalle reutiliza `GET /agents/{agentId}` y `GET /agents/{agentId}/history`, que ya están en el contrato. Falta la lista de pagos de comisión.
- **Liquidación** (`settlement`, `LQ-000007`; en pantalla "Pago de comisión") sobre el riel real. Es una entidad aparte, no una operación. Falta definir creación, `GET /settlements/{settlementId}` (el historial ya lo enlaza), estados `pending | paid | voided` y cómo aparecen en el historial. También el período (`periodo_desde` y `periodo_hasta` del esquema), la idempotencia, la regla de una sola liquidación pendiente por corresponsal y qué pasa con la comisión que se acumula mientras el pago está en curso. El esquema exige que el monto sea todo el saldo al crearla.

Produce: la sección admin en `contracts/relia-api.openapi.yaml`.

## Answer

Resuelto en grilling (1 ronda, 2026-10-16). Asset: [contracts/relia-api.openapi.yaml](../../../../contracts/relia-api.openapi.yaml), tags `admin` y `settlements` (Redocly: válido).

- **Pantalla de inicio admin en una llamada**: `GET /agents` (solo admin) devuelve `{ items, totals }`. Las filas son las de `v_resumen_admin_corresponsal`; los totales de la red cuentan solo depósitos y retiros completados, con el valor M. Sin paginación.
- **Crear una liquidación**: `POST /agents/{agentId}/settlements` con `Idempotency-Key` y sin cuerpo.
  - El monto es toda la comisión acumulada al crearla y queda congelado; lo que se acumule mientras se paga queda para la próxima.
  - El período va desde el día siguiente a la última liquidación pagada (o desde la primera operación) hasta hoy.
  - Errores: `409 settlement_in_progress`, `422 no_commission`. Si el riel no acepta el pago, responde `503` y no se crea nada.
- **Estados**: `pending | paid | voided` (con `voidReason`). Un pago de riel `failed` deja la liquidación `voided`, y reintentar es crear otra. No hay anulación manual. En el historial, `paid` aparece como `completed` y `voided` como `cancelled`.
- **Consultar**: `GET /settlements/{settlementId}` y `GET /settlements?from=&to=&agentId=&status=&cursor=`; la pantalla de inicio la usa con `from` = hoy. El rol `agent` solo ve las suyas.
- **Aviso al corresponsal**: al pasar a `paid`, el backend deja un SMS simulado `aviso_comision`. No tiene endpoint.
- **Permisos del admin**: lee todo y liquida, pero no opera por el corresponsal. Crear operaciones o ajustes, cancelar y confirmar entregas le responde 403.
