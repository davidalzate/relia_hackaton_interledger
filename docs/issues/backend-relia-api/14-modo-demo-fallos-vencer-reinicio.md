# 14: Modo demo: fallos forzados, "vencer ya" y reinicio

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** desde el panel del Modo demo, quien presenta fuerza que el próximo pago falle, vence una solicitud al instante y deja la demo en su estado inicial con un botón.

**Blocked by:** 10 (Carga de cupo por Bre-B y por Wallet), 13 (Liquidaciones)

**Status:** ready-for-human (implementado; probado contra un proxy falso. Falta correr la carpeta `demo` contra el proxy real)

- [x] `GET /demo/faults` y `PUT /demo/faults` `{ failNextPayment }` funcionan, con el interruptor en memoria.
- [x] Con el interruptor activo, el próximo depósito, retiro, carga o liquidación queda `failed`/`payment_failed` (o `voided`) sin llamar al riel, y el interruptor se apaga solo.
- [x] `POST /demo/operations/{operationId}/expire` cancela en el riel un retiro o una carga pendiente y lo deja `expired`/`request_expired`. Si no está pendiente responde `409`, y si el riel no responde, `503`.
- [x] `POST /demo/reset` responde `204` y:
  - cancela en el riel los cobros pendientes;
  - vacía operaciones, pagos, liquidaciones, llaves, OTP, mensajes, recibos e idempotencia con `TRUNCATE … RESTART IDENTITY CASCADE`;
  - vuelve a correr la siembra del ticket 01;
  - apaga los fallos forzados.
- [x] Después del reinicio, la referencia de la siguiente operación vuelve a empezar y `v_agent_balance_check` está en 0.
- [x] Todas las rutas `/demo/*` responden `404` sin `DEMO_MODE`.
- [ ] Verificado con la carpeta `demo`: cada botón y un reinicio con un retiro pendiente, que queda cancelado en el proxy.

## Notas de implementación

- **Interruptor** (`DemoFaults`, singleton): se apaga en el mismo paso en que se lee (`Interlocked.Exchange`), así dos peticiones simultáneas no lo usan las dos.
  - Se consume en la creación, después de validar (cupo, efectivo, comisión) y de guardar, justo antes de llamar al riel. Un 4xx no lo gasta.
  - La operación se cierra con `CloseWithoutMovementAsync` como `failed`/`payment_failed`, con origen `simulator`, un `payment_event` `simulate` y `failureDetail` "Fallo forzado desde el Modo demo". La reserva se libera sola y el retiro no envía el SMS de cobro.
  - Aplica también a la carga Bre-B, que no pasa por el riel, y a la liquidación con billetera simulada.
  - La liquidación queda `voided` con ese texto como `voidReason`, mediante `ISettlementSync.VoidAsync` (extraído del camino del riel).
  - `PUT` sin `failNextPayment` responde `422`.
- **"Vencer ya"** (`IOperationService.ExpireNowAsync`): reutiliza `IOperationSync.ExpireAsync` del ticket 11, sin esperar el plazo. Un depósito, una operación que no está pendiente o una que el pagador ya aprobó (`payment_not_cancellable`) dan `409 operation_not_cancellable`. Si el riel no responde, `503` y la operación sigue `pending`. La carga Bre-B se vence solo en el backend.
- **Reinicio** (`IDemoService.ResetAsync`):
  1. cancela en el riel cada pago `incoming` `pending` con id del proxy. Si uno falla (ya aprobado o riel caído), lo registra y sigue: el reinicio no se bloquea por el riel;
  2. `DemoSeeder.ResetAsync` hace `TRUNCATE` de las 16 tablas con `RESTART IDENTITY CASCADE` y vuelve a sembrar en la misma transacción. `SeedAsync` y `ResetAsync` comparten la siembra (`SeedTablesAsync`);
  3. apaga el interruptor.
- **Después del reinicio cambian los ids de admin y corresponsales** (la siembra usa `Guid.NewGuid()`): los tokens viejos apuntan a ids que ya no existen y hay que volver a iniciar sesión. Si molesta en la demo, la siembra puede usar ids fijos.
- **Verificado contra el proxy falso del ticket 13** (ahora con cobros `pending_approval` y cancelación):
  - **interruptor**: depósito, retiro, carga Bre-B, carga Wallet (Luis) y liquidación (Diana) quedaron `failed`/`payment_failed` o `voided`, con 0 llamadas al proxy. Después de cada uno el interruptor quedó en `false`. La comisión de Diana siguió en 1.500 y los descuadres en 0;
  - **vencer ya**:
    - un retiro → `200` `expired`/`request_expired` y `cancelled` en el proxy; repetirlo → `409`;
    - una carga Bre-B → `expired`;
    - un depósito → `409`; un id inexistente → `404`;
    - con el proxy en 503 → `503` y el retiro sigue `pending`;
  - **reinicio** con ese retiro pendiente y el interruptor encendido → `204`:
    - el retiro quedó `cancelled` en el proxy y el interruptor en `false`;
    - quedan 13 operaciones sembradas (OP-000001 a OP-000013), 0 liquidaciones, 0 mensajes, 0 registros de idempotencia, 1 llave y 4 corresponsales, con descuadres en 0;
    - la siguiente operación fue OP-000014, con una clave de idempotencia ya usada antes del reinicio;
  - **sin `DEMO_MODE`**: `GET`/`PUT /demo/faults`, `/demo/reset` y `/expire` → `404`.
- **Por decidir** (de la revisión):
  - **Ids fijos en la siembra.** Hoy, después de un reinicio, quien presenta tiene que volver a iniciar sesión. Con ids fijos los tokens sobrevivirían. No revisé si un token viejo da `401`/`404` limpio o `500` (por ejemplo, el FK del admin al liquidar).
  - **El interruptor se gasta aunque la transacción se deshaga.** Si después de consumirlo falla un `SaveChanges` o el commit, queda apagado sin operación fallida. Es poco probable; habría que volver a encenderlo.
  - **Reinicio con el riel caído.** Cada cobro pendiente espera el timeout del proxy (10 s) antes de seguir, y los que no se cancelan quedan vivos en el riel: podrían aprobarse después sin operación. Las cancelaciones van antes del `TRUNCATE`; un cobro creado en medio no se cancela.
  - **`/expire` responde `409` ante cualquier error inesperado del riel** (que no sea indisponibilidad), igual que el vencimiento del barrido.
  - **La lista de tablas del `TRUNCATE` está escrita a mano**: hay que actualizarla si se agrega una tabla.
