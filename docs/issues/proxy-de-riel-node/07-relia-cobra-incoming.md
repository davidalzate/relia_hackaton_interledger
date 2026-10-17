# 07: Relia cobra: `incoming` aprobado o rechazado

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El backend crea un pago `incoming` (retiro o carga por Wallet) y entrega `approval.url` al pagador. El pagador aprueba o rechaza en su billetera sin volver a Relia, y el proxy detecta el resultado solo. Dentro del `POST`: crea el incoming payment en la billetera de Relia con `expiresAt` y `metadata`, crea la quote en la billetera del pagador (grant no interactivo) y pide el grant interactivo `outgoing-payment` en el servidor de autorización del pagador (`limits.debitAmount` = débito de la quote, `limits.receiver` = el incoming, sin `finish`). El sincronizador consulta la continuación. Con el `access_token`, crea el outgoing payment en la billetera del pagador y confirma igual que en el ticket 04.

**Blocked by:** 04 (Relia paga)

**Status:** ready-for-agent

- [ ] `incoming` desde `jeffdev` con `expiresAt` a +5 min → `201 pending_approval` con `approval: { method: redirect, url }` y `debitAmount`.
- [ ] Sin `expiresAt`, o con uno en el pasado → `422 validation_error`. Las validaciones de contraparte del ticket 04 aplican igual.
- [ ] La `description` se ve en la pantalla de aprobación o en el cobro de la billetera del pagador.
- [ ] Al aprobar → `processing` (con `approval` en null) → `completed` con `receivedAmount`.
- [ ] Al rechazar → `rejected`, con el incoming payment de Relia cerrado.
- [ ] Si la quote venció al aprobar, se cotiza de nuevo una vez. Si la nueva quote supera el débito aprobado o vuelve a fallar → `failed` con `quote_expired`.
- [ ] Un `incoming` en `pending_approval` que se aprueba durante un reinicio del proxy avanza igual al volver.
