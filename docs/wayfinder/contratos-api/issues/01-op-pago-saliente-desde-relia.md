# Open Payments: cómo paga Relia desde su propia billetera sin aprobar cada pago

Type: research
Status: resolved
Blocked by:
Labels: wayfinder:research

## Question

En el depósito y en el pago de comisión, Relia envía dinero desde **su** billetera de la red de prueba de Interledger a otro payment pointer, y no debe haber un humano aprobando cada pago. Con el SDK `@interledger/open-payments` (Node):

- ¿Qué secuencia exacta de llamadas sirve (resolver wallet address, incoming payment en el receptor, quote, outgoing payment), y qué grants pide cada una?
- ¿El grant de `outgoing-payment` sobre la billetera de Relia exige interacción? Si la exige, ¿se puede obtener una vez con límites (`limits.debitAmount`, `interval`) y reutilizar o rotar el token entre pagos? ¿Cuánto dura el token y cómo se rota?
- ¿Cómo se sabe que el pago terminó (estados del outgoing payment, `failed`, `sentAmount`) y cada cuánto conviene hacer polling?
- ¿Cómo se convierte un payment pointer `$ilp.interledger-test.dev/x` en una wallet address URL?
- ¿Qué identificadores conviene que el proxy guarde para reconciliar?

Resultado esperado: el flujo paso a paso con referencias a la documentación primaria (openpayments.dev, repo del SDK) y los estados que el contrato de riel deberá mapear.

## Answer

Hallazgos completos en la rama local `research/op-pago-saliente-desde-relia` (commit 679ac9b), archivo `docs/research/op-pago-saliente-desde-relia.md`. El push al remoto falló por permisos y la rama solo existe en local.

- **El grant `outgoing-payment` siempre requiere interacción**, aunque sea sobre la billetera propia de Relia. Se pide **una sola vez** sin `receiver`, con `limits.debitAmount` y `limits.interval` (p. ej. `R/2026-10-16T00:00:00Z/P1D`), y un operador lo aprueba en la UI del test wallet. Desde ahí el proxy paga sin humano hasta agotar el tope del intervalo.
- **Secuencia por pago**: `walletAddress.get` de receptor y emisor, grant `incoming-payment` no interactivo en el servidor de autorización del receptor, `incomingPayment.create`, grant `quote` no interactivo en el de Relia, `quote.create` (vence en ~5 min) y `outgoingPayment.create({ quoteId })` con el token del grant aprobado.
- **Token**: dura lo que diga `expires_in` (en la doc son 10 min; leerlo siempre). Se rota con `token.rotate`, que revoca el anterior y funciona aunque haya vencido. El proxy debe serializar las rotaciones y guardar siempre el último `value` y `manage`.
- **Estado**: la API solo expone `failed` (boolean) y `sentAmount`. Puede haber pago parcial. El test wallet cancela por fondos insuficientes. El éxito se confirma con `completed=true` en el incoming payment del receptor. No hay webhooks para el cliente: polling. La cadencia de 1 s y después 3–5 s hasta ~2 min, pasando luego a `unknown`, es una propuesta del research, no una recomendación oficial.
- **Payment pointer**: `$ilp.interledger-test.dev/relia` → `https://ilp.interledger-test.dev/relia`. El SDK no lo normaliza; lo hace el proxy, que después usa `walletAddress.id`.
- **Implicaciones para el contrato**: estados `pending | completed | failed | unknown`; errores `grant_exhausted`, `needs_reauth`, `quote_expired` e `insufficient_funds`. Para reconciliar se guardan el id de la operación (también en `metadata`), los ids de incoming, quote y outgoing, y los montos.
- **Sin verificar**: el `expires_in` real, el error exacto al exceder el límite y que la ventana de aprobación del operador sea de 10 min en el test wallet.
