# Integrar GET /agents/{agentId}

Type: task
Status: open
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /agents/{agentId}`
- **Pantalla:** B1 Caja, B7 Efectivo, B8 Historial (resumen), D2 Detalle
- **Flujo:** [FLUJOS.md §2](../../../../contracts/FLUJOS.md#2-caja-inicio-del-corresponsal)
- **Reemplaza:** `AgentService.balances` / `AdminService` (señales locales)

## Aceptación

- [ ] `balances.quota`, `commission`, `cashOnHand`, `cashReserved` en pantalla.
- [ ] Avisos de bajo (< $50.000) calculados en el front con `LOW_BALANCE_THRESHOLD`.
- [ ] Se refresca tras cada operación terminal.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
