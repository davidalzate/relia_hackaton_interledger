# Integrar POST /demo/reset

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `POST /demo/reset`
- **Pantalla:** E1 "Reiniciar demo"
- **Flujo:** [FLUJOS.md §11](../../../../contracts/FLUJOS.md#11-modo-demo)
- **Reemplaza:** Reset de señales locales

## Aceptación

- [ ] 204 → recarga saldos, historial y lista de corresponsales.
- [ ] Se elimina el reset local.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
