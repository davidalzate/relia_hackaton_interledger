# 04: Relia paga: `outgoing` de punta a punta

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El backend crea un pago `outgoing` (depósito o liquidación) hacia un payment pointer, y el destino recibe exactamente `receiveAmount` sin aprobación humana. Dentro del `POST`: valida la contraparte, revisa que el permiso esté `active`, crea el incoming payment en el receptor (grant no interactivo, `metadata` con `idempotencyKey` y `description`), crea la quote en la billetera de Relia y crea el outgoing payment con el token del permiso. El sincronizador confirma el pago leyendo el incoming y el outgoing payment, con la cadencia de la spec y sin `unknown`. `GET /payments/{id}` responde desde el almacén. Montos con `BigInt`. En este ticket la idempotencia es básica: la `Idempotency-Key` es obligatoria y se guarda, y el ticket 05 completa la semántica.

**Blocked by:** 02 (Resolver billeteras), 03 (Permiso permanente de Relia)

**Status:** ready-for-agent

- [ ] `outgoing` a `jeffdev` por `"150000"` COP/2 → `201 processing` con `Location`, `debitAmount` y las tres URLs en `rail`. Después `completed`, con `receivedAmount` igual a `receiveAmount` y `completedAt`.
- [ ] `GET /payments/{id}` no llama al riel. `version` y `updatedAt` cambian con cada cambio de estado. Un id desconocido → `404 not_found`.
- [ ] La contraparte es la billetera de Relia → `422 same_wallet`. Monto o contraparte en otra moneda o escala → `422 unsupported_asset`. Pointer inexistente → `404 wallet_not_found`.
- [ ] Sin permiso `active` → `503 relia_grant_unavailable`, sin pago guardado.
- [ ] Si falla un paso del riel antes de crear el outgoing payment → `503`, sin pago guardado y con el incoming payment ya creado cerrado con `complete`.
- [ ] Un outgoing con `failed: true` → `failed` con `partial_payment` si `sentAmount > 0`, si no `rail_error` (o `insufficient_funds` cuando el riel lo dice explícitamente), y el mensaje en `failureDetail`.
- [ ] Un pago en `processing` se sigue consultando después de un reinicio.
