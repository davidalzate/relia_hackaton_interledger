# 05: Llaves, invitación por WhatsApp y bandeja simulada

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal valida el celular del usuario y, si no tiene llave, lo invita por WhatsApp. Quien presenta ve el mensaje en el teléfono simulado. Este ticket construye:

- el módulo de mensajería simulada sobre `simulated_messages`;
- los helpers de enmascarado;
- el interruptor `DEMO_MODE` para las rutas `/demo/*`.

**Blocked by:** 03 (Ajustes de efectivo e idempotencia)

**Status:** ready-for-agent

- [ ] `GET /keys/{phone}` responde `200` con `walletAddressMasked`, o `404 key_not_found`.
- [ ] `POST /keys/{phone}/invitations` (idempotente) responde `201` con `waLink`, `message` y `sentAt`.
- [ ] La invitación deja un mensaje `wallet_invitation` (`invitacion_wallet` en la API) con un `link` a la página pública que precarga el celular.
- [ ] Invitar un celular que ya tiene llave responde `409 key_already_linked`.
- [ ] `GET /demo/messages` acepta el filtro `phone`, va paginado (el más reciente primero) y trae `unreadCount` con el mismo filtro.
- [ ] `POST /demo/messages/read` responde `204` y marca como leídos los de un celular, o todos si no se envía celular.
- [ ] Sin `DEMO_MODE=true`, todas las rutas `/demo/*` responden `404`.
- [ ] El celular sale enmascarado como `300 *** 0001` y la billetera como `$ilp...test.dev/us***`.
- [ ] Los tipos de mensaje de la base se traducen a los del contrato.
- [ ] Verificado con las carpetas `keys` y `demo/messages`: `3000000001` → 200, `3000000002` → 404, invitación y mensaje en la bandeja.
