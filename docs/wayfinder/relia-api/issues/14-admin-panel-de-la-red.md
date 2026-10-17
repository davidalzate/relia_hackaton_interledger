# 14: Admin: panel de la red

Type: task
Status: resolved
Blocked by: 09
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el admin ve en una sola llamada todos los corresponsales con sus métricas y los totales de la red (D1), y abre el detalle de cada uno (D2).

- [ ] `GET /agents` (solo admin; el agente recibe `403`) devuelve `items` (`AgentSummary`, con `walletSimulated` en demo) y `totals` (`NetworkTotals`), sin paginación.
- [ ] Los montos movidos cuentan solo depósitos y retiros `completed`, con el monto solicitado (M).
- [ ] `commissionPaidToday` usa "hoy" en America/Bogota, con el reloj inyectable.
- [ ] El admin puede leer `GET /agents/{id}`, el historial y las operaciones de cualquier corresponsal.
- [ ] Hay una prueba que recorre todas las rutas de operar (crear depósito, retiro, carga y ajuste, cancelar, confirmar la entrega) y verifica `403 forbidden` para el admin.
