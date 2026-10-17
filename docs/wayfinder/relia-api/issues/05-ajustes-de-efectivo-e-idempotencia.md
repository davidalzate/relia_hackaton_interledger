# 05: Ajustes de efectivo e idempotencia

Type: task
Status: resolved
Blocked by: 04
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** el corresponsal suma efectivo a su caja o fija el total (pantalla B7) y ve el antes y el después. Este ticket también construye la idempotencia genérica que usan todas las creaciones.

- [ ] `POST /agents/{agentId}/cash-adjustments` con `mode: add` (amount > 0) o `mode: set` responde 201 con `CashAdjustment` y `cashOnHand.before/after`.
- [ ] Un `set` por debajo del efectivo reservado responde `422 insufficient_cash` con `shortfall`. Una variación de 0 responde `422 no_change`.
- [ ] El motivo por defecto es "Asignación de efectivo" o "Ajuste de efectivo".
- [ ] El ajuste cambia el efectivo en una transacción con bloqueo de la fila de cupos, a través del módulo Ledger de saldos (único lugar que escribe saldos).
- [ ] El ajuste aparece en el historial como `cashAdjustment`.
- [ ] El admin recibe `403 forbidden`.
- [ ] Idempotencia genérica:
  - misma clave y mismo cuerpo → misma respuesta, sin aplicar dos veces;
  - misma clave y otro cuerpo → `409 idempotency_conflict`;
  - si la respuesta fue 4xx o 503, no se guarda nada y se puede reintentar.
- [ ] Hay pruebas HTTP de cada caso.
