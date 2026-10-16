# 04: Caja: saldos e historial

Type: task
Status: open
Blocked by: 02
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal ve en su Caja el cupo, la comisión acumulada y el efectivo en caja, con sus reservas, y sus últimas operaciones. El admin puede leer cualquier corresponsal.

- [ ] `GET /agents/{agentId}` devuelve el `Agent` con `balances` completos. `quotaReserved` y `cashReserved` se calculan con las operaciones pendientes.
- [ ] Un `agent` que lee otro corresponsal recibe `403 forbidden`. Un corresponsal inexistente responde `404 not_found`. El `admin` lee cualquiera.
- [ ] `GET /agents/{agentId}/history` devuelve entradas unificadas, la más reciente primero, paginadas con `cursor`/`nextCursor` y `limit` (1–100, por defecto 20), con filtros `kind` y `status`.
- [ ] Las entradas no completadas muestran antes = después con el último saldo conocido.
- [ ] Pruebas HTTP de saldos, permisos, paginación y filtros, sembrando datos directamente.
