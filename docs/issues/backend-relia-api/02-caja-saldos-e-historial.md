# 02: Caja: saldos e historial

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal ve en su Caja el cupo, el efectivo en caja y la comisión acumulada, con sus reservas, y sus últimas entradas del historial. El admin puede leer cualquier corresponsal. El historial sale de la vista `v_agent_history`.

**Blocked by:** 01 (Base en Docker, mapeo en inglés, siembra, login y `/me`)

**Status:** ready-for-agent

- [ ] `GET /agents/{agentId}` devuelve el `Agent` con `balances` completos.
- [ ] `quotaReserved` es la suma del monto cobrado de los depósitos pendientes, y `cashReserved` la suma del neto de los retiros pendientes.
- [ ] Un `agent` que lee otro corresponsal recibe `403 forbidden`. Un corresponsal inexistente responde `404 not_found`. El `admin` lee cualquiera.
- [ ] `GET /agents/{agentId}/history` devuelve operaciones, ajustes y liquidaciones, la más reciente primero, con `cursor`/`nextCursor` y `limit` entre 1 y 100 (por defecto 20).
- [ ] Los filtros `kind` y `status` funcionan, y `float_load` sale como `quotaTopUp`.
- [ ] Las entradas no completadas muestran antes = después. Las liquidaciones `paid` salen `completed` y las `voided` salen `cancelled`.
- [ ] `history?limit=3` de un corresponsal sembrado con historial devuelve sus 3 últimas entradas, y Rosa devuelve una lista vacía.
- [ ] Verificado con la carpeta `agents/{agentId}` y `history` de la colección.
