# 11: Barrido de fondo y vencimiento de solicitudes

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** las operaciones pendientes avanzan aunque nadie las consulte. El backend refresca sus pagos en el proxy y vence las solicitudes pasadas de plazo, porque es el dueño del plazo de la operación.

**Blocked by:** 09 (Retiro, cancelación y entrega de efectivo)

**Status:** ready-for-agent

- [ ] Un servicio de fondo consulta cada 15 s los pagos de las operaciones pendientes y aplica la misma lógica que la lectura (versión, traducción de estados, completado).
- [ ] Al leer o en el barrido, una operación pasada de `expiresAt` (Wallet 5 min, Bre-B 10 min) se cancela en el riel si aplica y queda `expired`/`request_expired`, liberando la reserva.
- [ ] Si el proxy no responde, la operación sigue `pending` y se reintenta en el siguiente ciclo, sin que el servicio de fondo se caiga.
- [ ] El barrido tiene un punto de extensión para que las liquidaciones (ticket 13) registren su seguimiento.
- [ ] Los plazos usan `TimeProvider`.
- [ ] Verificado a mano:
  - un depósito completado en el riel pasa a `completed` sin consultar `GET /operations/{id}` (se ve en el historial);
  - un retiro sin aprobar queda `expired` a los 5 min.
