# 03: Ajustes de efectivo e idempotencia

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el corresponsal suma efectivo a su caja o fija el total, y ve el antes y el después. El ajuste queda en el historial. Este ticket también construye dos piezas que reutilizan los demás:

- el módulo Ledger de saldos, único lugar que escribe `agent_balances` y `ledger_entries`, en una transacción con `SELECT … FOR UPDATE`;
- la idempotencia genérica sobre `idempotency_records`, para toda creación con `Idempotency-Key`.

**Blocked by:** 02 (Caja: saldos e historial)

**Status:** ready-for-human (implementado; falta correr la colección de Postman)

- [x] `POST /agents/{agentId}/cash-adjustments` con `mode: add` (amount > 0) o `mode: set` responde `201` con `CashAdjustment`, `variation` y `cashOnHand.before/after`.
- [x] Un `set` por debajo del efectivo reservado responde `422 insufficient_cash` con `shortfall`. Una variación de 0 responde `422 no_change`.
- [x] El motivo por defecto es "Asignación de efectivo" (`add`) o "Ajuste de efectivo" (`set`).
- [x] El ajuste no toca cupo ni comisión. Aparece en el historial como `cashAdjustment` y `GET /agents/{id}` refleja el nuevo efectivo.
- [x] El admin recibe `403 forbidden`.
- [x] Idempotencia: misma clave y mismo cuerpo → misma respuesta, sin aplicar dos veces; misma clave y otro cuerpo → `409 idempotency_conflict`; si la respuesta fue 4xx o 503, no se guarda nada y se puede reintentar con la misma clave.
- [x] La idempotencia tiene alcance por ruta y actor: dos corresponsales pueden usar la misma clave.
- [x] `v_agent_balance_check` sigue en 0 después de varios ajustes.
- [ ] Verificado con la carpeta `cash-adjustments` de la colección, repitiendo la petición con la misma clave.

## Notas de implementación

- **Ledger (`ILedger`)**:
  - `LockAsync` toma la fila de `agent_balances` con `FOR UPDATE` y la recarga, porque si EF ya la tenía en memoria no pisa sus valores.
  - `Post` aplica la variación y agrega el movimiento con antes y después. En liquidaciones también sube `settled_commission_total`.
  - La siembra todavía escribe sus saldos directo; el resto del backend pasa por `ILedger`.
- **Idempotencia (`[Idempotent]`)**:
  - Es un filtro de acción que abre la transacción. `IUnitOfWork` se une a ella, así que la creación y su registro se confirman juntos o no queda ninguno. Un 4xx, un 503 o una excepción hacen rollback.
  - El alcance es método + plantilla de la ruta + actor, y la huella es el SHA-256 de los argumentos ya validados (cuerpo y valores de ruta). Por eso no importa cómo se escribió la URL: un id en mayúsculas cae en el mismo registro.
  - `pg_advisory_xact_lock` sobre (alcance, clave) serializa las peticiones simultáneas y se libera al cerrar la transacción.
  - Solo se guardan respuestas 2xx, con el header `Location` en `resource_id` (lo usarán las operaciones, que responden 202).
  - Al repetir, el JSON sale con las llaves en otro orden, porque `jsonb` no conserva el orden. El contenido es el mismo.
  - Por ahora solo lo usa `cash-adjustments`. Cada ticket de creación agrega `[Idempotent]` a su ruta.
- **`add` con monto 0** responde `422 validation_error` (campo `amount`), porque el contrato pide que sea mayor a 0.
- **`mode` y `amount` son obligatorios**: el DTO los declara nullable con `[Required]`. Antes, `{"mode":"set"}` fijaba el efectivo en 0. Los DTOs de los otros tickets (desglose, depósito, retiro, carga) tienen el mismo problema con sus enums y montos.
- **Errores de headers**: `errors[].field` de un header sale tal cual (`Idempotency-Key`); los de las propiedades del cuerpo salen en camelCase.
- **Verificado con `curl`**:
  - `add` y `set` con motivo propio y por defecto;
  - repetir con la misma clave devuelve la misma respuesta y no suma dos veces;
  - otro cuerpo con la misma clave → 409;
  - reintentar una clave después de un `no_change` → 201;
  - `insufficient_cash` con `shortfall` 40.000 frente a un retiro pendiente insertado por SQL;
  - admin y corresponsal ajeno → 403;
  - Luis puede usar la misma clave que Rosa;
  - 5 peticiones simultáneas con la misma clave aplican el ajuste una sola vez;
  - el historial y `v_agent_balance_check` quedan en 0.
