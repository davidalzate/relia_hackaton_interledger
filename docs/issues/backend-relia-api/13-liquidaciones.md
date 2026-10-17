# 13: Liquidaciones (pago de comisión)

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el admin paga toda la comisión acumulada de un corresponsal a su billetera y sigue el pago hasta `paid` o `voided`. El corresponsal recibe un aviso y ve la liquidación en su historial.

**Blocked by:** 11 (Barrido de fondo y vencimiento de solicitudes), 12 (Admin: panel de la red)

**Status:** ready-for-agent

- [ ] `POST /agents/{agentId}/settlements` (solo admin, idempotente) responde `202` con un `Settlement` `pending`, referencia `LQ-NNNNNN` y `Location`.
  - El monto es toda la comisión acumulada al crear y queda fijo.
  - El período va del día siguiente a la última liquidación pagada (o de la primera operación) a hoy en Bogotá.
  - `walletAddress` se copia del corresponsal.
- [ ] `409 settlement_in_progress` si ya hay una pendiente, `422 no_commission` si la comisión es $0 y `503 rail_unavailable` sin crear nada.
- [ ] Llama `POST /payments` outgoing a la billetera del corresponsal.
- [ ] Con `walletSimulated`, crea un pago `bre_b_simulated` simulado y pasa a `paid` sin llamar al riel.
- [ ] `completed` → `paid` con `paidAt`: la comisión acumulada baja el monto, `settledCommissionTotal` sube el monto, queda un movimiento y se envía un SMS de aviso de comisión.
- [ ] `failed` → `voided` con `voidReason`.
- [ ] El seguimiento corre en `GET /settlements/{id}` y en el barrido.
- [ ] `GET /settlements` filtra por `from`, `to`, `agentId` y `status`, va paginado y el más reciente primero. El agente solo ve las suyas y se ignora `agentId`.
- [ ] `GET /settlements/{id}` responde `403` a un agente ajeno y `404` si no existe.
- [ ] La liquidación aparece en el historial como `settlement`, y `v_agent_balance_check` queda en 0.
- [ ] Verificado con la carpeta `settlements`:
  - pago de la comisión de Rosa en la red de prueba hasta `paid`;
  - un corresponsal con billetera simulada;
  - el 409, el 422 y el 503 (proxy apagado o permiso sin aprobar).
