# Integrar GET /agents/{agentId}

Type: task
Status: resolved
PR: [#22](https://github.com/davidalzate/relia_hackaton_interledger/pull/22)
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:base
Feature: Base y sesión

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

## Notas

- D2 (admin) queda para la feature Admin: necesita los ids reales de `GET /agents` (ticket 20). `refresh()` no se llama al entrar a cada pantalla mientras haya operaciones en mock.
