# 05: Llaves, invitación por WhatsApp y bandeja simulada

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal valida el celular del usuario y, si no tiene llave, lo invita por WhatsApp. Quien presenta ve el mensaje en el teléfono simulado. Este ticket construye:

- el módulo de mensajería simulada sobre `simulated_messages`;
- los helpers de enmascarado;
- el interruptor `DEMO_MODE` para las rutas `/demo/*`.

**Blocked by:** 03 (Ajustes de efectivo e idempotencia)

**Status:** ready-for-human (implementado y probado con `curl`; falta correr la colección de Postman)

- [x] `GET /keys/{phone}` responde `200` con `walletAddressMasked`, o `404 key_not_found`.
- [x] `POST /keys/{phone}/invitations` (idempotente) responde `201` con `waLink`, `message` y `sentAt`.
- [x] La invitación deja un mensaje `wallet_invitation` (`invitacion_wallet` en la API) con un `link` a la página pública que precarga el celular.
- [x] Invitar un celular que ya tiene llave responde `409 key_already_linked`.
- [x] `GET /demo/messages` acepta el filtro `phone`, va paginado (el más reciente primero) y trae `unreadCount` con el mismo filtro.
- [x] `POST /demo/messages/read` responde `204` y marca como leídos los de un celular, o todos si no se envía celular.
- [x] Sin `DEMO_MODE=true`, todas las rutas `/demo/*` responden `404`.
- [x] El celular sale enmascarado como `300 *** 0001` y la billetera como `$ilp...test.dev/us***`.
- [x] Los tipos de mensaje de la base se traducen a los del contrato.
- [ ] Verificado con las carpetas `keys` y `demo/messages`: `3000000001` → 200, `3000000002` → 404, invitación y mensaje en la bandeja.

## Notas de implementación

- **`ISimulatedInbox`** (`SimulatedInbox`): único módulo de mensajería simulada.
  - `Send` agrega el mensaje sin guardarlo, para que quede en la misma transacción de quien lo envía. Lo van a usar el OTP, los avisos de depósito y comisión, las solicitudes de pago y los recibos.
  - `ListAsync` y `MarkReadAsync` alimentan `/demo/messages`.
  - La traducción de tipos de la base a los del contrato vive aquí (`DemoMessageType` con `JsonStringEnumMemberName`).
- **Paginación de la bandeja**: el cursor es el `id` del último mensaje (identity, crece con el tiempo) y el orden es `id` descendente. Un cursor que no es número responde `422 validation_error`.
- **`Masking`**: `Phone` y `WalletAddress`. La billetera conserva la primera etiqueta y los últimos 8 caracteres del dominio, y 2 letras de la ruta: `$ilp.interledger-test.dev/jeffdev` → `$ilp...test.dev/je***`. `GET /keys` devuelve el celular sin enmascarar, como dice el contrato (`KeyLookup.phone` es `Phone`). `Masking.Phone` lo usarán operaciones y recibos.
- **Enlace de la invitación**: `{Relia:PublicBaseUrl}/vincular?phone=…`. `/vincular` es la ruta de la página pública en el frontend, en lugar del `relia.com/selectkey` del ejemplo del contrato. El `waLink` es `https://wa.me/57{celular}?text=…` con el mensaje codificado. `sentAt` es el `created_at` del mensaje.
- **`[DemoOnly]`**: es un filtro de recurso sobre `DemoController`. Sin `DEMO_MODE=true` responde `404 not_found` en `problem+json`, antes de validar el modelo. Las rutas `/demo/*` van sin login (`[AllowAnonymous]`).
- **`POST /demo/messages/read`**: acepta un cuerpo vacío, `{}` o `{ "phone" }`. Un celular inválido responde `422`.
- **Verificado con `curl`** (con Luis, `DEMO_MODE` encendido y apagado en dos instancias):
  - `3000000001` → 200 con `$ilp...test.dev/je***`; `3000000002` → 404 `key_not_found`;
  - invitar a `3000000002` → 201. Repetir con la misma clave devuelve la misma respuesta y no crea un segundo mensaje. Invitar a `3000000001` → 409 `key_already_linked`. Sin `Idempotency-Key` → 422;
  - bandeja: filtro por celular, `limit=2` con `nextCursor`, `unreadCount` por filtro, y cursor o celular inválidos → 422;
  - `read` por celular, sin cuerpo y con `{}` → 204, y `unreadCount` baja;
  - con `DEMO_MODE=false`, `/demo/messages` y `/demo/messages/read` → 404 `not_found`.

  Quedaron 3 invitaciones en la bandeja de la base local, para `3000000002` (2) y `3000000003`.
