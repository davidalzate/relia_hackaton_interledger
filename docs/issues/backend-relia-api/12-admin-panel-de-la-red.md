# 12: Admin: panel de la red

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el admin ve en una sola llamada todos los corresponsales con sus métricas y los totales de la red, y abre el detalle de cada uno. Los datos salen de la vista `v_admin_agent_summary`.

**Blocked by:** 07 (Depósito de punta a punta)

**Status:** ready-for-agent

- [ ] `GET /agents` (solo admin; el agente recibe `403`) devuelve `items` (`AgentSummary`, con `walletSimulated`) y `totals` (`NetworkTotals`), sin paginación.
- [ ] Los montos movidos cuentan solo depósitos y retiros `completed`, con el monto solicitado (M).
- [ ] `commissionPayable` y `commissionPaidToday` se calculan con "hoy" en America/Bogota.
- [ ] El admin lee `GET /agents/{id}`, el historial, las operaciones y los recibos de cualquier corresponsal.
- [ ] El admin recibe `403 forbidden` en todas las rutas de operar: crear depósito, retiro, carga y ajuste, cancelar y confirmar la entrega.
- [ ] Verificado con la carpeta `agents` como admin: los totales cuadran con los depósitos y retiros hechos en los tickets anteriores, y cada ruta de operar devuelve 403.
