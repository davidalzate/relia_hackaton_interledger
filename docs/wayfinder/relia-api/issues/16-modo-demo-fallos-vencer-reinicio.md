# 16: Modo demo: fallos forzados, "vencer ya" y reinicio

Type: task
Status: resolved
Blocked by: 12, 15
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** desde el panel E1, quien presenta fuerza un fallo, vence una solicitud al instante y reinicia la demo a su estado inicial (flujo 11).

- [ ] `GET /demo/faults` y `PUT /demo/faults` `{ failNextPayment }`. Con el flag activo, el próximo depósito, retiro, carga o liquidación queda `failed`/`payment_failed` (o `voided`) sin llamar al riel, y el flag se apaga solo.
- [ ] `POST /demo/operations/{operationId}/expire` cancela en el riel un retiro o una carga pendiente y lo deja `expired`/`request_expired`. Si no está pendiente, responde 409. Si el riel no responde, responde 503.
- [ ] `POST /demo/reset` (204):
  - cancela en el riel los cobros pendientes;
  - vacía operaciones, pagos, liquidaciones, llaves, OTP, mensajes y recibos;
  - siembra 4 corresponsales con historiales de ejemplo (Rosa en ceros), el 3000000001 vinculado a `$ilp.interledger-test.dev/jeffdev` y el 3000000002 sin llave;
  - apaga los fallos forzados.
- [ ] Todas las rutas `/demo/*` responden 404 sin `DEMO_MODE`.
- [ ] Hay pruebas HTTP de cada botón.
