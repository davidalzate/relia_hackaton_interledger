# 02: Caja: saldos e historial

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal ve en su Caja el cupo, el efectivo en caja y la comisión acumulada, con sus reservas, y sus últimas entradas del historial. El admin puede leer cualquier corresponsal. El historial sale de la vista `v_agent_history`.

**Blocked by:** 01 (Base en Docker, mapeo en inglés, siembra, login y `/me`)

**Status:** ready-for-human (implementado; falta correr la colección de Postman)

- [x] `GET /agents/{agentId}` devuelve el `Agent` con `balances` completos.
- [x] `quotaReserved` es la suma del monto cobrado de los depósitos pendientes, y `cashReserved` la suma del neto de los retiros pendientes.
- [x] Un `agent` que lee otro corresponsal recibe `403 forbidden`. Un corresponsal inexistente responde `404 not_found`. El `admin` lee cualquiera.
- [x] `GET /agents/{agentId}/history` devuelve operaciones, ajustes y liquidaciones, la más reciente primero, con `cursor`/`nextCursor` y `limit` entre 1 y 100 (por defecto 20).
- [x] Los filtros `kind` y `status` funcionan, y `float_load` sale como `quotaTopUp`.
- [x] Las entradas no completadas muestran antes = después. Las liquidaciones `paid` salen `completed` y las `voided` salen `cancelled`. (`paid` → `completed` lo traduce la vista; sin probar con datos hasta el ticket 13.)
- [x] `history?limit=3` de un corresponsal sembrado con historial devuelve sus 3 últimas entradas, y Rosa devuelve una lista vacía.
- [ ] Verificado con la carpeta `agents/{agentId}` y `history` de la colección.

## Notas de implementación

- **Permisos**: un `agent` que pide un id ajeno recibe `403`, exista o no, para no revelar qué ids existen. El `404` solo lo ve el admin. La regla vive en una sola extensión de `ICurrentUser`, que reutilizan los demás tickets.
- **Historial**: SQL propio sobre `v_agent_history`, con un `LEFT JOIN` a `operations` para sacar `failureCode`, que la vista no trae.
  - El `id` de cada entrada es el `operation_id`, el `settlement_id` o `cash-{ledger_entry_id}`.
  - La página se ordena por (`occurred_at`, `id`) de forma descendente, y el cursor es ese par en base64url.
  - Un cursor inválido responde `422 validation_error` (campo `cursor`).
- **Verificado con `curl`** sobre la base sembrada, más filas de prueba insertadas por SQL en Luis (un depósito y un retiro pendientes, un depósito fallido, una liquidación anulada y otra pendiente):
  - reservas de 30.000 de cupo y 40.000 de efectivo;
  - antes = después en las entradas no completadas, y la anulada sale `cancelled` con su motivo;
  - la paginación de a 4 recorre las 12 entradas sin repetir;
  - filtros por `kind` y `status`;
  - `422` con `limit=0`, con un `kind` inválido y con un cursor inválido.

  La liquidación `paid` → `completed` no se probó con datos: la traduce la vista y no hay forma de crearla hasta el ticket 13.
