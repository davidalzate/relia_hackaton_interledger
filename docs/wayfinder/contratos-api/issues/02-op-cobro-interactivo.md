# Open Payments: cómo cobra Relia a la billetera de otra persona con aprobación interactiva

Type: research
Status: resolved
Blocked by:
Labels: wayfinder:research

## Question

En el retiro (y en la carga de cupo por Wallet) Relia le cobra a la billetera del usuario (o del corresponsal), y el dueño aprueba en su proveedor de billetera. Con el SDK `@interledger/open-payments` (Node) contra la red de prueba de Interledger:

- ¿Qué secuencia sirve? Incoming payment en la billetera de Relia, quote y grant interactivo de `outgoing-payment` en el servidor de autorización del pagador, con `interact.start: ["redirect"]`, y después el outgoing payment. ¿Quién crea el outgoing payment y con qué token?
- ¿Qué devuelve el grant interactivo (`interact.redirect`, `continue.uri`, `continue.access_token`, `wait`)? ¿Hace falta `interact.finish` (redirect de vuelta a una URI nuestra) o se puede hacer polling de la continuación sin finish URI?
- ¿Cómo se distingue "el usuario rechazó" de "todavía no responde"? ¿Qué pasa al vencer el grant?
- ¿Cómo se cancela una solicitud pendiente (revocar el grant o la continuación)? ¿Y un incoming payment sin pagar (`complete`, `expiresAt`)?
- ¿Se puede fijar el vencimiento a 5 minutos (`expiresAt` del incoming payment y vida del grant)?
- ¿Cómo se detecta el pago completado: `receivedAmount` del incoming payment o estado del outgoing?

Resultado esperado: el flujo paso a paso con estados y transiciones, referenciados a la documentación primaria, para que el contrato de riel exponga `approvalUrl`, `pending → completed | rejected | expired | cancelled | failed` y la cancelación.

## Answer

Hallazgos completos en la rama local `research/op-cobro-interactivo` (commit 6f54c38), archivo `docs/research/op-cobro-interactivo.md`. El push al remoto falló por permisos y la rama solo existe en local. Fuentes: spec OpenAPI de Open Payments, código de Rafiki, SDK de Node, frontend del test wallet y RFC 9635. Todo es lectura de código; no se probó contra la red real.

- **Quién hace qué**: Relia ejecuta todo con sus propias llaves. Crea el incoming payment en su billetera y la quote en la billetera del pagador (grants no interactivos), pide el grant interactivo `outgoing-payment` con `limits.debitAmount` de la quote y crea el outgoing payment en la billetera del pagador con el `access_token` que devuelve la continuación.
- **`approvalUrl`**: es `interact.redirect`. El grant también devuelve `continue.uri`, `continue.access_token` y `wait` (5 s por defecto; consultar antes da `too_fast`).
- **Finish o polling, uno de los dos**: sin `interact.finish` se hace polling con `POST continue` vacío. Con `finish`, Rafiki rechaza el polling ("grant cannot be polled"). El test wallet siempre redirige a finish; sin `finish` el usuario termina en una página 202 en blanco del servidor de autorización.
- **Respuestas del polling**: 200 con solo `continue` = pendiente; 200 con `access_token` = aprobado; 401 `request_denied` = rechazado.
- **El vencimiento no se ve en el polling**: Rafiki sigue respondiendo "pendiente" después del vencimiento (600 s por defecto), así que el proxy necesita su propio temporizador de 5 min.
- **5 minutos**: el `expiresAt` del incoming payment se puede fijar en +5 min. La vida del grant no la elige el cliente. La quote vence a los ~5 min, y si vence antes de la aprobación hay que volver a cotizar (`InvalidQuote`).
- **Cancelar**: antes de aprobar, `DELETE continue.uri` más `incomingPayment.complete`. Un outgoing payment ya creado no se puede cancelar.
- **Completado**: se detecta con el incoming payment (`completed` o `receivedAmount >= incomingAmount`). El fallo sale de `failed: true` en el outgoing payment. No hay webhooks para el cliente: polling.
- **Sin verificar en `auth.interledger-test.dev`**: los valores reales de `wait`, del vencimiento de la interacción y de la vida de la quote, y si la quote requiere interacción.
- **Queda para el contrato de riel**: usar `finish` con un callback al proxy (mejor UX y aviso inmediato del rechazo, pero sin polling) o polling sin finish (con la página en blanco).
