# 14: Modo demo: fallos forzados, "vencer ya" y reinicio

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** desde el panel del Modo demo, quien presenta fuerza que el próximo pago falle, vence una solicitud al instante y deja la demo en su estado inicial con un botón.

**Blocked by:** 10 (Carga de cupo por Bre-B y por Wallet), 13 (Liquidaciones)

**Status:** ready-for-agent

- [ ] `GET /demo/faults` y `PUT /demo/faults` `{ failNextPayment }` funcionan, con el interruptor en memoria.
- [ ] Con el interruptor activo, el próximo depósito, retiro, carga o liquidación queda `failed`/`payment_failed` (o `voided`) sin llamar al riel, y el interruptor se apaga solo.
- [ ] `POST /demo/operations/{operationId}/expire` cancela en el riel un retiro o una carga pendiente y lo deja `expired`/`request_expired`. Si no está pendiente responde `409`, y si el riel no responde, `503`.
- [ ] `POST /demo/reset` responde `204` y:
  - cancela en el riel los cobros pendientes;
  - vacía operaciones, pagos, liquidaciones, llaves, OTP, mensajes, recibos e idempotencia con `TRUNCATE … RESTART IDENTITY CASCADE`;
  - vuelve a correr la siembra del ticket 01;
  - apaga los fallos forzados.
- [ ] Después del reinicio, la referencia de la siguiente operación vuelve a empezar y `v_agent_balance_check` está en 0.
- [ ] Todas las rutas `/demo/*` responden `404` sin `DEMO_MODE`.
- [ ] Verificado con la carpeta `demo`: cada botón y un reinicio con un retiro pendiente, que queda cancelado en el proxy.
