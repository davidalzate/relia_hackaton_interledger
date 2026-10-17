# 03: Ajustes de efectivo e idempotencia

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal suma efectivo a su caja o fija el total, y ve el antes y el después. El ajuste queda en el historial. Este ticket también construye dos piezas que reutilizan los demás:

- el módulo Ledger de saldos, único lugar que escribe `agent_balances` y `ledger_entries`, en una transacción con `SELECT … FOR UPDATE`;
- la idempotencia genérica sobre `idempotency_records`, para toda creación con `Idempotency-Key`.

**Blocked by:** 02 (Caja: saldos e historial)

**Status:** ready-for-agent

- [ ] `POST /agents/{agentId}/cash-adjustments` con `mode: add` (amount > 0) o `mode: set` responde `201` con `CashAdjustment`, `variation` y `cashOnHand.before/after`.
- [ ] Un `set` por debajo del efectivo reservado responde `422 insufficient_cash` con `shortfall`. Una variación de 0 responde `422 no_change`.
- [ ] El motivo por defecto es "Asignación de efectivo" (`add`) o "Ajuste de efectivo" (`set`).
- [ ] El ajuste no toca cupo ni comisión. Aparece en el historial como `cashAdjustment` y `GET /agents/{id}` refleja el nuevo efectivo.
- [ ] El admin recibe `403 forbidden`.
- [ ] Idempotencia: misma clave y mismo cuerpo → misma respuesta, sin aplicar dos veces; misma clave y otro cuerpo → `409 idempotency_conflict`; si la respuesta fue 4xx o 503, no se guarda nada y se puede reintentar con la misma clave.
- [ ] La idempotencia tiene alcance por ruta y actor: dos corresponsales pueden usar la misma clave.
- [ ] `v_agent_balance_check` sigue en 0 después de varios ajustes.
- [ ] Verificado con la carpeta `cash-adjustments` de la colección, repitiendo la petición con la misma clave.
