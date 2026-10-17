# 01: Base en Docker, mapeo en inglés, siembra, login y `/me`

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** quien clona el repo levanta PostgreSQL con `docker compose up` y arranca el backend sin tocar configuración. Rosa y el admin ya pueden iniciar sesión y restaurar la sesión con `GET /me`. Este ticket deja la base que usan todos los demás.

- **DDL**: `db_ddl.sql` pasa a correr en un Postgres 16 limpio y sigue siendo válido en Azure. Se quita `AUTHORIZATION azure_pg_admin`, las funciones van antes que sus triggers y se borran las secuencias sueltas. Se agregan `idempotency_records`, `operations.failure_code`, `operations.cash_handed_over_at`, `agents.wallet_simulated`, `payments.rail_payment_id` y `payments.rail_version`.
- **Mapeo**: el mapeo de EF se reescribe a los nombres en inglés de ese DDL (tablas, columnas y enums de PostgreSQL).
- **Siembra**: una única rutina crea la tarifa vigente, un admin, los 4 corresponsales con sus historiales de ejemplo (Rosa en ceros) y la llave `3000000001 → $ilp.interledger-test.dev/jeffdev`. Corre al arrancar si no hay admins.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `docker compose up` sobre un volumen nuevo aplica `db_ddl.sql` sin errores. Borrar el volumen y volver a levantar deja la base en limpio.
- [ ] El DDL contiene las tablas y columnas agregadas que lista la spec, y `protect_operation` permite actualizar `cash_handed_over_at` después de completar.
- [ ] El backend arranca en `http://localhost:5080` con la cadena de conexión por defecto al contenedor y siembra si la base está vacía.
- [ ] La siembra deja la tarifa 1.000/500/500, un admin, 4 corresponsales, la llave del `3000000001` y el `3000000002` sin llave.
- [ ] Las contraseñas sembradas se guardan con hash, y los historiales de ejemplo pasan los triggers de la base.
- [ ] Después de sembrar, `v_agent_balance_check` da los tres descuadres en 0.
- [ ] `POST /auth/login` devuelve `accessToken`, `expiresIn`, `role` y `agentId` (solo para `agent`).
- [ ] Con credenciales malas o una cuenta inactiva responde `401 invalid_credentials`. Con un cuerpo inválido responde `422 validation_error` con `errors`.
- [ ] `GET /me` devuelve el perfil y, si el rol es `agent`, el corresponsal con saldos. Sin token responde `401 unauthorized` en `problem+json`.
- [ ] La configuración tiene la URL y la API key del proxy, el payment pointer de Relia, `DEMO_MODE` y la URL pública. Los secretos van fuera del repo.
- [ ] Verificado con la carpeta `auth` y `me` de la colección de Postman.
