# 13: Liquidaciones (pago de comisión)

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** el admin paga toda la comisión acumulada de un corresponsal a su billetera y sigue el pago hasta `paid` o `voided`. El corresponsal recibe un aviso y ve la liquidación en su historial.

**Blocked by:** 11 (Barrido de fondo y vencimiento de solicitudes), 12 (Admin: panel de la red)

**Status:** ready-for-human (implementado; probado contra un proxy falso. Falta pagar la comisión de Rosa en la red de prueba y correr la colección)

- [x] `POST /agents/{agentId}/settlements` (solo admin, idempotente) responde `202` con un `Settlement` `pending`, referencia `LQ-NNNNNN` y `Location`.
  - El monto es toda la comisión acumulada al crear y queda fijo.
  - El período va del día siguiente a la última liquidación pagada (o de la primera operación) a hoy en Bogotá.
  - `walletAddress` se copia del corresponsal.
- [x] `409 settlement_in_progress` si ya hay una pendiente, `422 no_commission` si la comisión es $0 y `503 rail_unavailable` sin crear nada.
- [x] Llama `POST /payments` outgoing a la billetera del corresponsal.
- [x] Con `walletSimulated`, crea un pago `bre_b_simulated` simulado y pasa a `paid` sin llamar al riel.
- [x] `completed` → `paid` con `paidAt`: la comisión acumulada baja el monto, `settledCommissionTotal` sube el monto, queda un movimiento y se envía un SMS de aviso de comisión.
- [x] `failed` → `voided` con `voidReason`.
- [x] El seguimiento corre en `GET /settlements/{id}` y en el barrido.
- [x] `GET /settlements` filtra por `from`, `to`, `agentId` y `status`, va paginado y el más reciente primero. El agente solo ve las suyas y se ignora `agentId`.
- [x] `GET /settlements/{id}` responde `403` a un agente ajeno y `404` si no existe.
- [x] La liquidación aparece en el historial como `settlement`, y `v_agent_balance_check` queda en 0.
- [ ] Verificado con la carpeta `settlements`:
  - pago de la comisión de Rosa en la red de prueba hasta `paid`;
  - un corresponsal con billetera simulada;
  - el 409, el 422 y el 503 (proxy apagado o permiso sin aprobar).

## Notas de implementación

- **Creación** (`SettlementService.CreateAsync`, `[Idempotent]`, solo admin):
  - bloquea `agent_balances` (FOR UPDATE) y, dentro del bloqueo, revisa que no haya una pendiente (`409`) y que la comisión sea > 0 (`422`). `ux_settlements_one_pending` queda de respaldo en la base;
  - el monto es `accrued_commission` en ese momento y `destination_payment_pointer` = `agents.payment_pointer`;
  - período: desde `period_end + 1` de la última pagada; si no hay, desde el día (Bogotá) de la primera operación completada; si tampoco, hoy. Se recorta a hoy, porque `ck_settlement_period` exige `period_end ≥ period_start` (pasa si se liquida dos veces el mismo día);
  - el pago usa el mismo id idempotente que las operaciones (`RailPayments.PaymentId`, con tipo `settlement`): reintentar tras un 503 con la misma clave pide al proxy el mismo pago;
  - descripción del pago: "Comisión de {corresponsal} en Relia", sin el LQ (cambia en cada reintento).
- **Billetera simulada**: solo con `DEMO_MODE` y `walletSimulated`. Crea el pago `bre_b_simulated` outgoing, con `rail_details = {"simulated": true}`, `rail_reference = SIM-LQ-…` y un `payment_event` `simulate` de origen `system`, y lo paga en la misma petición: la respuesta `202` ya trae `paid`. Sin `DEMO_MODE`, el corresponsal se liquida por el riel.
- **Seguimiento** (`ISettlementSync`, en `SettlementSync.cs`):
  - misma traducción que las operaciones: `completed` → `paid`; `failed`, `rejected`, `expired` y `cancelled` → `voided`;
  - `voidReason` = `failureDetail` del proxy, o su `failureCode`, o "El pago terminó en {status}";
  - descarta lecturas con `version` ≤ `rail_version` y respeta el máximo de una consulta por segundo por pago (`RailRefreshThrottle`);
  - al pagar sigue el orden de los triggers: pago `completed` → liquidación `paid` → movimiento `settlement` (la comisión baja el monto; `Ledger` sube `settled_commission_total`) → SMS `commission_notice` al celular del corresponsal;
  - corre en `GET /settlements/{id}` y en el barrido (`SettlementSweepTask`, un `ISweepTask` más).
- **Listado**:
  - `from` y `to` son días en Bogotá, inclusive, sobre `created_at`;
  - el cursor es `created_at` en ticks UTC de la última fila;
  - el agente siempre ve las suyas: su `agentId` reemplaza el filtro.
- **Refactor**: lo que operaciones y liquidaciones hacen igual con el riel pasó de `OperationService` y `OperationSync` a `Services/RailPayments.cs`:
  - el id idempotente del pago;
  - la traducción de errores al crear (503, `wallet_not_found`, `idempotency_conflict`);
  - el registro de cada lectura en `payment_events`;
  - la copia de los datos del proxy a `payments`;
  - el recorte de la descripción.
- **Eliminado**: `Implementations.cs` y `FeatureInDevelopmentException` (501), porque ya no quedan servicios sin implementar. "Hoy en Bogotá" quedó en `Services/Bogota.cs`.
- **Verificado contra un proxy falso** (`POST /payments` responde `processing`, con controles para completar, fallar o responder 503):
  - **Luis (`walletSimulated`)**: `202` `paid` por $9.500, período del 2026-10-11 (primera operación) al 2026-10-16. Quedó el movimiento `settlement` con comisión −9.500, `settled_commission_total` en 9.500, el pago `bre_b_simulated` completado y el SMS. Descuadres en 0. Repetir la misma clave devuelve el mismo `202`; otra clave da `422 no_commission`;
  - **Rosa por el riel**: `422 no_commission` en $0. Le di $1.000 de comisión con una carga Bre-B y dos depósitos. Con el proxy en 503 → `503 rail_unavailable` y 0 liquidaciones en la base. La misma clave, con el proxy arriba → `202` `pending` por $1.000; el proxy recibió `outgoing` a `$…/rosa-test` por `100000`. Otra liquidación → `409 settlement_in_progress`;
  - completado el pago en el proxy, **el barrido** (cada 3 s en la prueba) la pasó a `paid` sin que nadie la leyera. Comisión de 1.000 a 0, liquidada en 1.000, SMS y descuadres en 0. El historial de Rosa la muestra como `settlement` `completed` con comisión 1.000 → 0;
  - otra de $500 con el pago fallido → `GET /settlements/{id}` la devuelve `voided` con el motivo del proxy, la comisión sigue en 500 y el pago queda `failed`;
  - **permisos y errores**: el agente → `403` al crear; un corresponsal inexistente → `404`; sin `Idempotency-Key` → `422`;
  - **listado**: filtros por `status`, `agentId`, `from`/`to` (hoy, ayer, mañana) y `limit=2` con `nextCursor` correctos. Un cursor inválido → `422`. Luis con `agentId` de Rosa solo ve la suya; leer una de Rosa → `403`; una inexistente → `404`;
  - **panel admin** (`GET /agents`): `commissionPaidToday` = 10.500.

  Quedaron en la base local LQ-000001 (Luis), LQ-000003 y LQ-000004 (Rosa, contra el proxy falso) y OP-000075 a OP-000077 de Rosa. LQ-000002 se consumió en el 503: la secuencia no retrocede.
- **Pendiente**: pagar la comisión de Rosa en la red de prueba con el permiso de Relia aprobado, y el 503 con el permiso sin aprobar en el proxy real.
- **Por decidir**:
  - **`from`/`to` filtran por `created_at`.** El inicio admin usa `from` = hoy para "Pagos de comisión", mientras `commissionPaidToday` de `GET /agents` cuenta por `paid_at`. Una liquidación creada ayer y pagada hoy suma en el panel pero no sale en la lista. Hoy no pasa: las simuladas se pagan al crearse y las del riel en segundos. Si importa, el filtro puede ir sobre `COALESCE(paid_at, created_at)`.
  - **Reintento tras un 503 con comisión nueva.** Si entre el 503 y el reintento con la misma clave se completa un depósito, el monto cambia. El proxy ve otro cuerpo con la misma `Idempotency-Key` y responde `409 idempotency_conflict`, aunque la petición al API sea idéntica. La salida es reintentar con otra clave. En las operaciones no pasa, porque el monto viene en la petición.
  - **Cursor sin desempate por id.** Dos liquidaciones con el mismo `created_at` (microsegundos) podrían saltarse al paginar. No lo cambié.
