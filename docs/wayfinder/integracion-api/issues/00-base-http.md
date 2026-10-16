# Integrar Base HTTP: cliente, auth, idempotencia, errores y polling

Type: task
Status: open
Blocked by: 
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** — (infraestructura)
- **Pantalla:** Todas
- **Flujo:** [FLUJOS.md §Reglas comunes](../../../../contracts/FLUJOS.md#reglas-comunes)
- **Reemplaza:** Mocks con `of(...).pipe(delay)` en `core/services/*`

## Aceptación

- [ ] `authInterceptor` añade `Authorization: Bearer <token>` salvo `/auth/login`, `/key-links*` y `/demo/*`.
- [ ] Helper de `Idempotency-Key`: misma clave al reintentar tras 4xx/503; clave nueva tras `failed|expired|cancelled`.
- [ ] `errorInterceptor` parsea `application/problem+json` y expone `code` tipado (enum del contrato).
- [ ] Helper de polling (1–2 s) hasta estado terminal, reutilizable para operaciones y liquidaciones.
- [ ] Schemas zod de respuestas en `shared/schemas/` alineados a `relia-api.openapi.yaml`.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
