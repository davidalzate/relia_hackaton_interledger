# Integrar GET /keys/{phone}

Type: task
Status: resolved
PR: [#28](https://github.com/davidalzate/relia_hackaton_interledger/pull/28)
Blocked by: [00](00-base-http.md)
Labels: wayfinder:task, frontend, feature:depositar
Feature: Depositar (B2)

## Question

Conectar el frontend (`frontend/relia`) al endpoint real de la Relia API y retirar el mock.

- **Endpoint:** `GET /keys/{phone}`
- **Pantalla:** B2.2, B3.1 Validar llave
- **Flujo:** [FLUJOS.md §5](../../../../contracts/FLUJOS.md#5-depositar)
- **Reemplaza:** `AgentService.validateKey(phone, simulateMissing)`

## Aceptación

- [ ] 200 → muestra `walletAddressMasked`.
- [ ] `404 key_not_found` → abre B5 (invitación).
- [ ] Se elimina el flag `simulateMissing` (se usa el 3000000002 real).
- [ ] Test unitario del servicio con `HttpTestingController` (éxito y error principal).

## Notas

- También se quitó el interruptor de simulación en Retirar (comparten `validateKey`).
