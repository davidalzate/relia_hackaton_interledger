# 05: Idempotencia de `POST /payments`

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El backend puede reintentar la creación de un pago sin miedo a duplicarlo. El registro de idempotencia guarda la clave más una huella del cuerpo normalizado. Un candado en memoria por clave serializa las creaciones simultáneas. Aplica a las dos direcciones.

**Blocked by:** 04 (Relia paga)

**Status:** ready-for-agent

- [ ] La misma `Idempotency-Key` con el mismo cuerpo → `200` con el pago en su estado actual (mismo `id`).
- [ ] La misma clave con otro cuerpo (otro monto, otra contraparte) → `409 idempotency_conflict`.
- [ ] Dos `POST` simultáneos con la misma clave crean un solo pago en el riel, y los dos reciben el mismo `id`.
- [ ] Una creación que terminó en `503` no deja registro, y el reintento con la misma clave crea el pago.
- [ ] Sin `Idempotency-Key`, o con una que no es UUID → `422 validation_error`.
- [ ] `idempotencyKey` vuelve como eco en el `Payment`, distinto del `id` del proxy.
