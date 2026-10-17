# 15: Liquidaciones (pago de comisión)

Type: task
Status: resolved
Blocked by: 13, 14
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el admin paga toda la comisión acumulada de un corresponsal a su billetera y sigue el pago hasta `paid` o `voided` (D3, flujo 10).

- [ ] `POST /agents/{agentId}/settlements` (solo admin) responde 202 con un `Settlement` `pending` y referencia `LQ-NNNNNN`.
  - El monto es toda la comisión al crear y queda fijo.
  - El período va desde el día siguiente a la última liquidación pagada (o desde la primera operación) hasta hoy.
  - `walletAddress` se copia del corresponsal.
- [ ] `409 settlement_in_progress` si ya hay una pendiente. `422 no_commission` si la comisión es $0. `503 rail_unavailable` sin crear nada.
- [ ] Llama `POST /payments` outgoing al corresponsal. Si tiene `walletSimulated`, pasa a `paid` sin riel.
- [ ] `completed` → `paid` (baja la comisión acumulada, sube `settledCommissionTotal` y se envía un SMS `aviso_comision`). `failed` → `voided` con `voidReason`.
- [ ] El seguimiento está en la lectura y en el barrido de fondo.
- [ ] `GET /settlements` filtra por `from`, `to`, `agentId` y `status`, paginado. El agente solo ve las suyas y se ignora `agentId`.
- [ ] `GET /settlements/{id}` responde 403 a un agente ajeno.
- [ ] La liquidación aparece en el historial como `settlement` (`paid` → `completed`, `voided` → `cancelled`).
- [ ] Hay pruebas HTTP de cada caso.
