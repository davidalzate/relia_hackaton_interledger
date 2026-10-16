# Integrar POST /key-links

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /key-links`
- **Pantalla:** C1.1 Vincular billetera (pública)
- **Flujo:** [FLUJOS.md §8](../../../../contracts/FLUJOS.md#8-vincular-billetera-página-pública)
- **Reemplaza:** `LinkPhoneComponent` (flujo local)

## Aceptación

- [ ] Body `{ phone, walletAddress }` → 201 con `challengeId`, `expiresAt` (5 min), `maxAttempts`.
- [ ] "Reenviar código" vuelve a llamar (invalida el desafío anterior).
- [ ] Errores: `409 key_already_linked`, `409 wallet_already_linked`, `409 phone_reserved`, `422 wallet_not_found`, `429 rate_limited` (respeta `Retry-After`).
- [ ] Sin token (excluido en el interceptor).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
