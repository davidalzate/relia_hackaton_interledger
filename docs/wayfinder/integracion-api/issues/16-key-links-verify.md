# Integrar POST /key-links/{challengeId}/verify

Type: task
Status: resolved
PR: [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32)
Blocked by: [15](15-key-links.md)
Labels: wayfinder:task, frontend, feature:llaves
Feature: Llave inexistente y vinculación (B5, C1)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /key-links/{challengeId}/verify`
- **Pantalla:** C1.2 Código OTP
- **Flujo:** [FLUJOS.md §8](../../../../contracts/FLUJOS.md#8-vincular-billetera-página-pública)
- **Reemplaza:** `LinkPhoneComponent` (OTP local)

## Aceptación

- [ ] Body `{ code }` → 200 con `walletAddress` y `linkedAt`.
- [ ] `422 invalid_otp` muestra `attemptsRemaining`; `otp_expired`, `otp_attempts_exceeded` con su texto.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- El OTP lo verifica el API con `POST /key-links/{challengeId}/verify`, que crea la llave. Antes se simulaba en el navegador y la llave nunca se creaba.
