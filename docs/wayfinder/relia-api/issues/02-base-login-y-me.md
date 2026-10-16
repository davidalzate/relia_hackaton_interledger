# 02: Base del backend, login y perfil de sesión

Type: task
Status: open
Blocked by: None (can start immediately)
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal y el admin inician sesión con correo y contraseña y el frontend restaura la sesión con `GET /me`. Este ticket reemplaza la plantilla `WeatherForecast` y deja lista la base que usan todos los demás: Postgres con el esquema v0.2, JWT, errores `problem+json` y el proyecto de pruebas.

- [ ] Se elimina la plantilla `WeatherForecast`. El backend arranca contra PostgreSQL con el esquema v0.2 aplicado.
- [ ] `POST /auth/login` devuelve `accessToken`, `expiresIn`, `role` y `agentId` (solo para `agent`). Con credenciales malas responde `401 invalid_credentials`. Con un cuerpo inválido responde `422 validation_error` con `errors`.
- [ ] `GET /me` devuelve el perfil y, si el rol es `agent`, el corresponsal con saldos. Sin token responde `401 unauthorized`.
- [ ] Hay una política de autorización por rol (`agent`, `admin`) y por propiedad del corresponsal, que los demás tickets reutilizan.
- [ ] Hay un único mapeo de errores de dominio a `application/problem+json` con `code` del enum del contrato.
- [ ] Siembra mínima: un admin y un corresponsal, con contraseñas hasheadas.
- [ ] El proyecto de pruebas tiene `WebApplicationFactory`, Postgres en Testcontainers, `TimeProvider` falso y un doble en memoria de `IRailClient` controlable desde la prueba.
- [ ] Hay pruebas HTTP de login (éxito y fallo), `/me` y 401.
