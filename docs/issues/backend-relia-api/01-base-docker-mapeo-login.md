# 01: Base en Docker, mapeo en inglés, siembra, login y `/me`

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** quien clona el repo levanta PostgreSQL con `docker compose up` y arranca el backend sin tocar configuración. Rosa y el admin ya pueden iniciar sesión y restaurar la sesión con `GET /me`. Este ticket deja la base que usan todos los demás.

- **DDL**: `db_ddl.sql` pasa a correr en un Postgres 16 limpio y sigue siendo válido en Azure. Se quita `AUTHORIZATION azure_pg_admin`, las funciones van antes que sus triggers y se borran las secuencias sueltas. Se agregan `idempotency_records`, `operations.failure_code`, `operations.cash_handed_over_at`, `agents.wallet_simulated`, `payments.rail_payment_id` y `payments.rail_version`.
- **Mapeo**: el mapeo de EF se reescribe a los nombres en inglés de ese DDL (tablas, columnas y enums de PostgreSQL).
- **Siembra**: una única rutina crea la tarifa vigente, un admin, los 4 corresponsales con sus historiales de ejemplo (Rosa en ceros) y la llave `3000000001 → $ilp.interledger-test.dev/jeffdev`. Corre al arrancar si no hay admins.

**Blocked by:** None (can start immediately)

**Status:** ready-for-human (implementado; falta correr la colección de Postman)

- [x] `docker compose up` sobre un volumen nuevo aplica `db_ddl.sql` sin errores. Borrar el volumen y volver a levantar deja la base en limpio.
- [x] El DDL contiene las tablas y columnas agregadas que lista la spec, y `protect_operation` permite actualizar `cash_handed_over_at` después de completar.
- [x] El backend arranca en `http://localhost:5080` con la cadena de conexión por defecto al contenedor y siembra si la base está vacía.
- [x] La siembra deja la tarifa 1.000/500/500, un admin, 4 corresponsales, la llave del `3000000001` y el `3000000002` sin llave.
- [x] Las contraseñas sembradas se guardan con hash, y los historiales de ejemplo pasan los triggers de la base.
- [x] Después de sembrar, `v_agent_balance_check` da los tres descuadres en 0.
- [x] `POST /auth/login` devuelve `accessToken`, `expiresIn`, `role` y `agentId` (solo para `agent`).
- [x] Con credenciales malas o una cuenta inactiva responde `401 invalid_credentials`. Con un cuerpo inválido responde `422 validation_error` con `errors`.
- [x] `GET /me` devuelve el perfil y, si el rol es `agent`, el corresponsal con saldos. Sin token responde `401 unauthorized` en `problem+json`.
- [x] La configuración tiene la URL y la API key del proxy, el payment pointer de Relia, `DEMO_MODE` y la URL pública. Los secretos van fuera del repo.
- [ ] Verificado con la carpeta `auth` y `me` de la colección de Postman.

## Notas de implementación

- **Puerto**: el contenedor publica en **5433**, porque en la máquina de desarrollo ya había un Postgres local en 5432. Se cambia con `RELIA_DB_PORT`.
- **Verificación hecha**: con `curl` contra el backend corriendo, después de un `docker compose down -v` y un `docker compose up` en limpio:
  - login de Rosa y del admin (el correo no distingue mayúsculas);
  - credenciales malas, correo desconocido y cuenta inactiva → `401 invalid_credentials`;
  - cuerpo inválido → `422` con `errors` en camelCase;
  - `/me` de Rosa (en ceros), de Luis (con saldos) y del admin;
  - `/me` sin token y con token inválido → `401 unauthorized`.

  `protect_operation` se probó en SQL: deja registrar la entrega una sola vez, solo en un retiro completado. Falta correr la colección de Postman.
- **Siembra**: las billeteras de los otros tres corresponsales no existen en el riel (`wallet_simulated`). Rosa usa `rosa-test`, que sí existe en la red de prueba.
- **Mapeo**: el modelo de EF declara las llaves foráneas que el DDL ya tiene. Sin ellas, EF no ordena los INSERT de una misma transacción y la base los rechaza.
