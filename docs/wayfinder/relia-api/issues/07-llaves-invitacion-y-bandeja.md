# 07: Consulta de llave, invitación por WhatsApp y bandeja simulada

Type: task
Status: resolved
Blocked by: 02
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal valida el celular del usuario y, si no tiene llave, lo invita por WhatsApp (B5). Quien presenta ve el mensaje en el teléfono simulado (C2).

- [ ] `GET /keys/{phone}` responde 200 con `walletAddressMasked`, o `404 key_not_found`.
- [ ] `POST /keys/{phone}/invitations` (con `Idempotency-Key`) responde 201 con `waLink`, `message` y `sentAt`. Escribe un mensaje `invitacion_wallet` con un `link` a la página pública que precarga el celular. Si el celular ya tiene llave, responde 409.
- [ ] Módulo de mensajería simulada sobre `mensajes_simulados`, reutilizable por otros tickets.
- [ ] `GET /demo/messages` (filtro `phone` opcional, paginado, `unreadCount`) y `POST /demo/messages/read` (204) funcionan sin login y solo con `DEMO_MODE=true`. Sin el flag responden 404.
- [ ] Hay helpers de enmascarado de celular (`300 *** 0001`) y billetera (`$ilp...test.dev/us***`).
- [ ] Hay pruebas HTTP de cada caso, incluido el 404 sin `DEMO_MODE`.
