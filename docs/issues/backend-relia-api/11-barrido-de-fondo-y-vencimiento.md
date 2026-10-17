# 11: Barrido de fondo y vencimiento de solicitudes

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** las operaciones pendientes avanzan aunque nadie las consulte. El backend refresca sus pagos en el proxy y vence las solicitudes pasadas de plazo, porque es el dueño del plazo de la operación.

**Blocked by:** 09 (Retiro, cancelación y entrega de efectivo)

**Status:** ready-for-human (implementado y verificado a mano; falta correr la colección)

- [x] Un servicio de fondo consulta cada 15 s los pagos de las operaciones pendientes y aplica la misma lógica que la lectura (versión, traducción de estados, completado).
- [x] Al leer o en el barrido, una operación pasada de `expiresAt` (Wallet 5 min, Bre-B 10 min) se cancela en el riel si aplica y queda `expired`/`request_expired`, liberando la reserva.
- [x] Si el proxy no responde, la operación sigue `pending` y se reintenta en el siguiente ciclo, sin que el servicio de fondo se caiga.
- [x] El barrido tiene un punto de extensión para que las liquidaciones (ticket 13) registren su seguimiento.
- [x] Los plazos usan `TimeProvider`.
- [x] Verificado a mano:
  - un depósito completado en el riel pasa a `completed` sin consultar `GET /operations/{id}` (se ve en el historial);
  - un retiro sin aprobar queda `expired` a los 5 min.

## Notas de implementación

- **`SweepService`** (`Services/Sweep.cs`) es un `BackgroundService` con `PeriodicTimer` sobre `TimeProvider`, cada `Relia:SweepIntervalSeconds` (15; mínimo 1).
  - En cada ciclo abre un scope y corre todos los `ISweepTask` registrados.
  - Un fallo se registra y se reintenta en el siguiente ciclo: el servicio no se cae.
- **Punto de extensión**: `ISweepTask`. Las operaciones son `OperationSweepTask`. Las liquidaciones (ticket 13) registran el suyo con `AddScoped<ISweepTask, …>()`.
  - `OperationSweepTask` lista los ids pendientes (la más vieja primero) y, para cada uno, en su propio scope, llama `IOperationSync.RefreshAsync`: la misma lógica que la lectura (versión, traducción, completado).
- **Vencimiento** (`IOperationSync.ExpireAsync`): el backend es dueño del plazo.
  - `RefreshAsync`, al leer y en el barrido, vence la operación pendiente cuyo pago pasó `expires_at` (Wallet 5–6 min, Bre-B 10 min). Usa el mismo turno de un segundo por pago que la consulta.
  - Con pago en el proxy, pide `POST /payments/{id}/cancel` fuera de la transacción. Bloquea la operación y el mismo pago, y la deja `expired`/`request_expired` (origen `system`), liberando la reserva. La respuesta del riel queda en `payment_events` (`expire`).
  - Bre-B (sin proxy) se vence localmente.
  - `payment_not_cancellable` (el pagador aprobó justo a tiempo) → no vence: sigue su curso y el refresco la completa.
  - Si el riel ya la había completado o fallado, manda el riel.
  - Proxy caído → sigue `pending` y se reintenta.
  - `not_found` (el proxy no conoce el pago) → se vence en el backend.
  - Otro error inesperado → se registra y se reintenta.
- **Cancelación perdida**: si la respuesta del cancel del vencimiento se pierde (timeout), una lectura posterior trae `cancelled`. Pasado el plazo, y sin que la pidiera el corresponsal, se registra como `expired`/`request_expired`.
  - Caso límite: un corresponsal que cancela a las 4:59 y recibe un `503` vería `expired` en vez de `cancelled`.
- **Para el ticket 14**: `ExpireAsync` no mira el reloj ("vencer ya") y lanza `RailUnavailableException` si el riel no responde (→ `503`). Devuelve `false` en tres casos distintos (no pendiente, `payment_not_cancellable`, el riel cerró antes). El endpoint necesitará distinguirlos.
- **Verificado**:
  - **Contra un proxy falso**, con barrido cada 3 s:
    - un depósito marcado `completed` en el proxy pasa a `completed` sin ningún `GET /operations`, y aparece en el historial con antes y después;
    - un retiro sin aprobar con el plazo vencido (forzado por SQL) queda `expired`/`request_expired`, con el pago cancelado en el proxy y el evento `pending→expired(system)`;
    - Bre-B vencida → `expired`;
    - un retiro en `processing` y vencido sigue `pending` y luego se completa;
    - un `GET` de una operación vencida devuelve `expired`;
    - proxy congelado (`SIGSTOP`) → la operación sigue `pending`, con avisos de reintento, el backend responde y no se cae ningún ciclo; al volver el proxy → `expired`;
    - el proxy no conoce el pago (reinicio del proxy falso) → `expired`;
    - un cancel a mano antes del plazo sigue dando `cancelled`;
    - descuadres en 0.
  - **Contra el proxy real**, con barrido cada 15 s: un retiro real de $10.000 (OP-000064, plazo 04:41:00 UTC) se dejó sin aprobar y sin consultarlo. A las 04:41:03 quedó `expired`/`request_expired`. Esta vez el temporizador del proxy reportó `expired` v2 antes que el backend, y el barrido lo aplicó (`pending→expired(rail)`).
    - Las líneas `sin respuesta: not_found` de su bitácora salen de una segunda instancia del backend que, durante la prueba, barría la misma base contra el proxy falso.
- **Ruido conocido**: mientras una operación está pendiente, cada refresco deja un `payment_event` (normalmente "(descartada)"): uno cada 15 s por operación, más uno por segundo si el frontend la consulta.

