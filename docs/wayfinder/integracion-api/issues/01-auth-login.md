# Integrar POST /auth/login

Type: task
Status: claimed
PR: [#20](https://github.com/davidalzate/relia_hackaton_interledger/pull/20)
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:base
Feature: Base y sesión

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /auth/login`
- **Pantalla:** A1 Login
- **Flujo:** [FLUJOS.md §1](../../../../contracts/FLUJOS.md#1-login-y-sesión)
- **Reemplaza:** `AuthService.login` (usuarios mock)

## Aceptación

- [ ] Guarda `accessToken`, `role` y `agentId`.
- [ ] `401 invalid_credentials` → "Correo o contraseña incorrectos."
- [ ] Redirige: `agent` → Caja, `admin` → Inicio admin.
- [ ] Logout solo descarta el token (no hay endpoint).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
