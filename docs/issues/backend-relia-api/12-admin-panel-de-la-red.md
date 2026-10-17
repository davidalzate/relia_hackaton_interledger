# 12: Admin: panel de la red

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el admin ve en una sola llamada todos los corresponsales con sus métricas y los totales de la red, y abre el detalle de cada uno. Los datos salen de la vista `v_admin_agent_summary`.

**Blocked by:** 07 (Depósito de punta a punta)

**Status:** ready-for-human (implementado y verificado con curl contra la base local. Falta correr la carpeta `agents` de Postman)

- [x] `GET /agents` (solo admin; el agente recibe `403`) devuelve `items` (`AgentSummary`, con `walletSimulated`) y `totals` (`NetworkTotals`), sin paginación.
- [x] Los montos movidos cuentan solo depósitos y retiros `completed`, con el monto solicitado (M).
- [x] `commissionPayable` y `commissionPaidToday` se calculan con "hoy" en America/Bogota.
- [x] El admin lee `GET /agents/{id}`, el historial, las operaciones y los recibos de cualquier corresponsal.
- [x] El admin recibe `403 forbidden` en todas las rutas de operar: crear depósito, retiro, carga y ajuste, cancelar y confirmar la entrega.
- [ ] Verificado con la carpeta `agents` como admin: los totales cuadran con los depósitos y retiros hechos en los tickets anteriores, y cada ruta de operar devuelve 403.

## Notas de implementación

- **`GET /agents`**: vive en `AgentsController` con ruta `~/agents` y `[Authorize(Roles = admin)]`. El servicio también llama a `EnsureAdmin` (regla nueva en `CurrentUserAccess`).
- **Datos**: `IAgentRepository.ListSummariesAsync` lee `v_admin_agent_summary` con SQL propio, une `agents` para `wallet_simulated` y ordena por nombre. Las sumas de la vista son `numeric` y se castean a `bigint`/`int`.
- **Mapeo**: `quota` = `float_balance`, `cashOnHand` = `expected_cash`, `accruedCommission` = `commission_payable`, `settledCommissionTotal` = `commission_paid_total`, `operations` = depósitos + retiros completados.
- **Totales**: se suman en el servicio a partir de las filas. `totalMoved` es `{count, amount}` de depósitos + retiros, `commissionPayable` es la suma de `accruedCommission`.
- **"Hoy"**: `commission_paid_today` lo calcula la vista con `now() AT TIME ZONE 'America/Bogota'`, no con `TimeProvider`. Hoy da igual porque el reloj es el del sistema. Si el Modo demo llega a mover el reloj, hay que pasarle la fecha a la consulta.
- **Lecturas y 403 del admin**: ya estaban desde los tickets 02, 07, 08 y 09 (`EnsureCanReadAgent`, `EnsureCanOperateAgent` y `[Authorize(Roles = agent)]` en las rutas de operar). No hubo que cambiarlas.
- **Verificado con curl** contra la base local, con el admin `admin@relia.co`:
  - `GET /agents` → `200` con los 4 corresponsales. Totales: 12 depósitos por $690.000, 12 retiros por $612.000, comisión por pagar $12.000. Cuadran con una consulta directa a `operations` y `agent_balances`;
  - como agente → `403`; sin token → `401`;
  - el admin lee el perfil, el historial, una operación y su recibo de Luis → `200`;
  - el admin en depósito, retiro, carga, ajuste, cancelar y entrega → `403 forbidden` en `problem+json`.
