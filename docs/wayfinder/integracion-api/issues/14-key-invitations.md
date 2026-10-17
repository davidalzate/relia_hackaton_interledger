# Integrar POST /keys/{phone}/invitations

Type: task
Status: claimed
PR: [#32](https://github.com/davidalzate/relia_hackaton_interledger/pull/32)
Blocked by: [10](10-keys-get.md)
Labels: wayfinder:task, frontend, feature:llaves
Feature: Llave inexistente y vinculación (B5, C1)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /keys/{phone}/invitations` + `Idempotency-Key`
- **Pantalla:** B5 Invitación WhatsApp
- **Flujo:** [FLUJOS.md §7](../../../../contracts/FLUJOS.md#7-llave-inexistente-invitación-por-whatsapp)
- **Reemplaza:** `InvitationService.send` / `whatsappLink` / `INVITE_MESSAGE`

## Aceptación

- [ ] 201 → usa `waLink` y `message` del backend (se borra el texto armado en el front).
- [ ] Tras volver, revalida con `GET /keys/{phone}`.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- Simulado por decisión del equipo (demo): sin `POST /keys/{phone}/invitations` ni WhatsApp real. C2 abre C1 con `?phone=`.
