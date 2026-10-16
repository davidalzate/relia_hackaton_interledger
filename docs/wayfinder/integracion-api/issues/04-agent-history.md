# Integrar GET /agents/{agentId}/history

Type: task
Status: open
Blocked by: [03](03-agent-detail.md)
Labels: wayfinder:task, frontend

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /agents/{agentId}/history?limit&cursor&kind`
- **Pantalla:** B1 (últimas 3), B8 Historial, D2
- **Flujo:** [FLUJOS.md §9](../../../../contracts/FLUJOS.md#9-historial-y-comprobante)
- **Reemplaza:** `AgentService.operations` / `recentOperations`

## Aceptación

- [ ] B1 pide `limit=3`.
- [ ] B8 pagina con `cursor`/`nextCursor`.
- [ ] Filtros `kind=deposit|withdrawal|quotaTopUp|cashAdjustment|settlement` ("Efectivo" = `cashAdjustment`, "Pago de comisión" = `settlement`).
- [ ] Fila con `operationId` abre `GET /operations/{id}`; con `settlementId` abre `GET /settlements/{id}`.
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).
