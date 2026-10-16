# 08: Vencimiento y cancelación de cobros

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** Un cobro que nadie aprueba vence a tiempo, y el backend puede retirarlo mientras no se haya aprobado. El vencimiento es una fecha límite guardada que el sincronizador revisa en cada vuelta, no un `setTimeout`. Al llegar `expiresAt` en `pending_approval`: `grant.cancel` sobre la continuación, `incomingPayment.complete` y `expired`. `POST /payments/{id}/cancel` hace lo mismo y deja el pago en `cancelled`, bajo un candado por pago para no cruzarse con el sincronizador.

**Blocked by:** 07 (Relia cobra)

**Status:** ready-for-agent

- [ ] Un `incoming` con `expiresAt` a +1 min sin aprobar → `expired`. Después, el enlace ya no deja pagar.
- [ ] El vencimiento se cumple aunque el proxy se haya reiniciado en medio.
- [ ] `cancel` de un `incoming` en `pending_approval` → `200 cancelled`. Un segundo `cancel` → `200` con el mismo pago y sin llamar al riel.
- [ ] `cancel` de un `outgoing`, de un pago ya aprobado o en `processing`, o de un pago en otro estado final → `409 payment_not_cancellable`.
- [ ] Un `401` al cancelar la continuación (ya finalizada) no impide cerrar el cobro. Si el riel no responde → `503 rail_unavailable` y el pago queda como estaba.
- [ ] Una aprobación que llega a la vez que un `cancel` termina en uno solo de los dos estados, nunca en los dos.
