# Integrar GET /demo/messages

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:demo
Feature: Modo demo (E1, C2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /demo/messages?phone=`
- **Pantalla:** C2 Teléfono: Mensajes, Ver SMS (OTP)
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** `SmsService` (bandeja local)

## Aceptación

- [ ] Sin `phone` lista todos; contador con `unreadCount`.
- [ ] Muestra `invitacion_wallet` (con `link`) y `otp`.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
