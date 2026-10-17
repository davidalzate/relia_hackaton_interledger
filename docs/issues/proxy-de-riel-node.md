# Proxy de riel en Node: adaptador de Open Payments que implementa `rail-proxy.openapi.yaml`

Type: spec
Status: open
Blocked by:
Labels: ready-for-agent

## Problem Statement

El backend .NET de Relia necesita mover dinero real en la red de prueba de Interledger para el depósito, el retiro, la carga de cupo por Wallet y el pago de comisión. Por diseño ([ADR 0001](../adr/0001-proxy-como-contrato-de-riel.md)), el backend no conoce Open Payments: no pide grants GNAP, no cotiza, no crea incoming ni outgoing payments y no guarda llaves de billetera. Solo habla un contrato de riel agnóstico ([contracts/rail-proxy.openapi.yaml](../../contracts/rail-proxy.openapi.yaml)): "cobrar a una billetera", "pagar a una billetera", consultar y cancelar.

Ese contrato ya está cerrado y la colección de Postman ya existe, pero nadie lo implementa. Sin el proxy, el backend no puede completar ninguna operación sobre el riel, el guion de demo no se puede presentar y el corresponsal no ve dinero real moverse.

## Solution

Un servicio Node con TypeScript, en su propia carpeta `rail-proxy/` en la raíz del repo, que implementa al pie de la letra `rail-proxy.openapi.yaml` sobre el SDK oficial `@interledger/open-payments`. Es el único componente que conoce Open Payments y el único que guarda las llaves de la billetera de Relia.

Desde el backend:
- `POST /payments` con `outgoing` hace que Relia pague a un payment pointer usando su **permiso permanente**. El pago nace en `processing`, sin que nadie apruebe nada.
- `POST /payments` con `incoming` hace que Relia cobre a un payment pointer. El pago nace en `pending_approval` con una `approval.url`. El pagador aprueba en su billetera y el proxy detecta solo si aprobó, rechazó o se venció el plazo.
- `GET /payments/{id}` devuelve al instante el último estado conocido. El proxy consulta Open Payments en segundo plano.
- `POST /payments/{id}/cancel` retira un cobro mientras no se haya aprobado.
- `GET /wallet-addresses` confirma que un pointer existe y devuelve su moneda y escala.

Desde el operador: `POST /relia-grant` pide el permiso permanente de la billetera de Relia y devuelve una `approvalUrl` para aprobarlo una sola vez. `GET /relia-grant` y `GET /health` dicen si el riel está listo para pagar.

## User Stories

### Backend .NET: pagar (`outgoing`)

1. Como backend, quiero crear un pago `outgoing` hacia el payment pointer del usuario con un `receiveAmount` fijo, para que en un depósito el usuario reciba exactamente el monto neto.
2. Como backend, quiero crear un pago `outgoing` hacia la billetera del corresponsal, para liquidarle la comisión acumulada.
3. Como backend, quiero que un `outgoing` nazca en `processing` sin aprobación humana, para que el depósito avance mientras el usuario sigue frente a la caja.
4. Como backend, quiero recibir `503 relia_grant_unavailable` cuando Relia no tiene permiso activo, agotado o pendiente de aprobación, para marcar la operación como fallida sin haber movido dinero.
5. Como backend, quiero que un `503` signifique siempre "no se creó ni cambió nada", para reintentar con la misma `Idempotency-Key` sin riesgo de pagar dos veces.
6. Como backend, quiero ver `debitAmount` (lo que sale de Relia según la cotización) y `receivedAmount` (lo que llegó), para conciliar el costo del riel.
7. Como backend, quiero que un `outgoing` termine en `completed` solo cuando el destino recibió el monto completo, para no emitir un recibo antes de tiempo.
8. Como backend, quiero que un `outgoing` fallido traiga `failureCode` (`insufficient_funds`, `quote_expired`, `relia_grant_unavailable`, `partial_payment`, `rail_error`) y `failureDetail`, para mostrar el motivo y darle a soporte el mensaje del riel.

### Backend .NET: cobrar (`incoming`)

9. Como backend, quiero crear un pago `incoming` a la billetera del usuario con `expiresAt`, para cobrarle un retiro con plazo de 5 minutos.
10. Como backend, quiero crear un pago `incoming` a la billetera del corresponsal, para la carga de cupo por Wallet.
11. Como backend, quiero recibir `approval: { method: "redirect", url }` en la respuesta de creación, para entregar el enlace por SMS simulado y mostrarlo como enlace o QR.
12. Como backend, quiero que `incoming` sin `expiresAt`, o con `expiresAt` en el pasado, responda `422 validation_error`, para no abrir cobros sin plazo.
13. Como backend, quiero que el pago pase solo a `processing` cuando el pagador aprueba, para mostrar "Pago aprobado, procesando" sin hacer nada más.
14. Como backend, quiero que el pago pase a `rejected` cuando el pagador rechaza, para traducirlo a operación `failed` con `customer_rejected`.
15. Como backend, quiero que el proxy venza el cobro al llegar `expiresAt`, cancele la aprobación en el riel y cierre el cobro, para que el pagador no pueda aprobar después del plazo.
16. Como backend, quiero que `approval` deje de venir (null) en cuanto el pago sale de `pending_approval`, para no seguir mostrando un enlace que ya no sirve.
17. Como backend, quiero que el `description` que envío se vea en la billetera del pagador, para que sepa qué está aprobando (por ejemplo "Retiro OP-000123 · Tienda Doña Rosa").

### Backend .NET: consultar, cancelar, idempotencia

18. Como backend, quiero que `GET /payments/{id}` responda sin llamar al riel, para consultarlo hasta una vez por segundo por pago y en un barrido cada 15 s sin cargar Open Payments.
19. Como backend, quiero que cada cambio de un pago suba `version` y `updatedAt`, para descartar lecturas viejas.
20. Como backend, quiero repetir `POST /payments` con la misma `Idempotency-Key` y el mismo cuerpo y recibir el pago existente con `200`, para reintentar después de un timeout sin duplicar.
21. Como backend, quiero recibir `409 idempotency_conflict` si reuso una `Idempotency-Key` con otro cuerpo, para detectar mis propios bugs.
22. Como backend, quiero que dos `POST` simultáneos con la misma `Idempotency-Key` creen un solo pago en el riel, para que un doble clic no cobre dos veces.
23. Como backend, quiero cancelar un `incoming` en `pending_approval` y recibir `200` con el pago `cancelled`, para cancelar un retiro desde la caja.
24. Como backend, quiero recibir `200` con el mismo pago si cancelo uno ya cancelado, para que cancelar sea idempotente.
25. Como backend, quiero recibir `409 payment_not_cancellable` si cancelo un `outgoing`, un pago ya aprobado o uno en otro estado final, para saber que el pago sigue su curso.
26. Como backend, quiero `404 not_found` para un `paymentId` desconocido, para distinguirlo de un error del riel.
27. Como backend, quiero ver `sender` y `receiver` como payment pointers, y en `rail` las URLs de incoming payment, quote y outgoing payment, para guardarlos en `pagos` y conciliar.

### Backend .NET: validación de billeteras

28. Como backend, quiero resolver un payment pointer con `GET /wallet-addresses?pointer=` y recibir `url`, `assetCode` y `assetScale`, para validar la billetera al vincular una llave.
29. Como backend, quiero `404 wallet_not_found` cuando el pointer no existe, para mostrar "Esa billetera no existe".
30. Como backend, quiero `422 validation_error` cuando el pointer está mal formado, para no confundir un error de digitación con una billetera inexistente.
31. Como backend, quiero `422 unsupported_asset` cuando el monto o la contraparte no están en la moneda y escala de la billetera de Relia, para no mover dinero en otra moneda.
32. Como backend, quiero `422 same_wallet` cuando la contraparte es la billetera de Relia, para no crear pagos de Relia a Relia.
33. Como backend, quiero `404 wallet_not_found` al crear un pago hacia un pointer inexistente, para fallar la operación antes de recibir efectivo.
34. Como backend, quiero `401 unauthorized` en toda ruta (salvo `/health`) cuando falta `X-Api-Key` o no coincide, para que nadie más en la red mueva dinero de Relia.
35. Como backend, quiero que todo error llegue como `application/problem+json` con un `code` del enum del contrato, para programar contra el `code` y no contra el texto.

### Operador de Relia

36. Como operador, quiero pedir el permiso permanente con un tope (`debitLimit`) y un intervalo ISO 8601 repetitivo, y recibir `approvalUrl` con `202`, para aprobarlo una sola vez en la billetera de Relia.
37. Como operador, quiero que el proxy detecte solo que aprobé el permiso, sin redirect de vuelta, para no tener que pegar ningún código en ningún lado.
38. Como operador, quiero ver en `GET /relia-grant` el estado (`missing`, `awaiting_approval`, `active`, `exhausted`), el tope, el intervalo y `approvedAt`, para saber si los depósitos van a funcionar.
39. Como operador, quiero que un permiso nuevo aprobado reemplace al anterior, para subir el tope sin reiniciar el proxy.
40. Como operador, quiero que el permiso aprobado sobreviva a un reinicio del proxy, para no volver a aprobarlo cada vez que se reinicia.
41. Como operador, quiero que el proxy rote el token del permiso solo, para no volver a aprobar cuando vence el token.
42. Como operador, quiero ver el estado `exhausted` cuando se agota el tope del intervalo, y que vuelva a `active` al empezar el siguiente, para saber por qué fallan los depósitos y cuándo vuelven.
43. Como operador, quiero que `GET /health` diga `ok` o `degraded`, la billetera de Relia y el estado del permiso, sin `X-Api-Key`, para revisar antes de presentar que todo está listo.
44. Como operador, quiero que `/health` responda `429` si lo consultan en exceso, para que un monitor mal configurado no tumbe el proxy.

### Pagador (usuario o corresponsal)

45. Como pagador, quiero abrir el enlace de aprobación y ver en mi billetera el monto exacto que se me debita, para aprobar con confianza.
46. Como pagador, quiero que si rechazo no se me cobre nada, para tener el control.
47. Como pagador, quiero que después del plazo el enlace ya no me deje pagar, para no pagar un retiro que el corresponsal ya canceló.

### Desarrollador y presentador del demo

48. Como desarrollador, quiero configurar el proxy solo con variables de entorno (puerto, API key, payment pointer de Relia, key ID y llave privada en base64, ruta de la base local), para correrlo igual en mi máquina y en el demo.
49. Como desarrollador, quiero que el proxy falle al arrancar, con un mensaje claro, si falta una variable o la llave no corresponde a la billetera de Relia, para no descubrirlo en medio del demo.
50. Como desarrollador, quiero que los logs muestren cada llamada al riel (pago, paso, código HTTP) sin tokens ni llaves, para depurar sin filtrar secretos.
51. Como desarrollador, quiero que los pagos en curso sigan avanzando después de un reinicio, para no dejar operaciones colgadas en `pending`.
52. Como presentador, quiero probar el proxy de punta a punta con la colección de Postman contra la red de prueba real, para confiar en él antes del guion.

## Implementation Decisions

### Proyecto y stack

- Carpeta propia `rail-proxy/` en la raíz del repo, con su propio `package.json`, al lado de `frontend/`. No es parte del backend .NET.
- Node 22 LTS y TypeScript. Servidor HTTP con Fastify. SDK `@interledger/open-payments` (v7.x): `createAuthenticatedClient` con la wallet address de Relia, el key ID y la llave privada Ed25519 decodificada desde base64.
- **El YAML manda.** El proxy implementa `rail-proxy.openapi.yaml` sin cambiarlo. Si algo no cuadra, primero se corrige el YAML y se regenera la colección, como dice [contracts/README.md](../../contracts/README.md).
- Validación de entrada derivada de los esquemas del contrato (mismos `pattern`, `enum`, `maxLength`). Cualquier falla de esquema se responde como `422 validation_error` en `problem+json`, no con el 400 por defecto de Fastify.
- Puerto 4000 por defecto (`servers` del contrato).

### Configuración (variables de entorno)

- Puerto, API key compartida, payment pointer de la billetera de Relia (`$ilp.interledger-test.dev/relia-test1` en el demo), key ID, llave privada en base64, ruta del archivo de base de datos y, como opcionales, las cadencias de consulta.
- Al arrancar, el proxy resuelve la billetera de Relia y guarda su `id`, `authServer`, `resourceServer`, `assetCode` y `assetScale`. Si no la resuelve o falta una variable obligatoria, se niega a arrancar.

### Módulos

1. **Config**: lee y valida el entorno y decodifica la llave.
2. **Capa HTTP**: rutas del contrato, middleware de `X-Api-Key` (comparación en tiempo constante; `/health` es público), validación, traducción de errores de dominio a `problem+json` con `type: about:blank`, `title`, `status` y `code`, cabecera `Location` en el `201` y rate limit solo en `/health`.
3. **Resolvedor de billeteras**: convierte payment pointer ↔ URL (`$host/ruta` → `https://host/ruta`; sin ruta → `/.well-known/pay`), llama a `walletAddress.get` y guarda el resultado en caché unos minutos. Distingue "no existe" (404 del servidor de billeteras → `wallet_not_found`) de "el riel no responde" (red o 5xx → `rail_unavailable`). Compara billeteras por su `id` canónico, no por el texto del pointer (así se detecta `same_wallet`).
4. **Gestor de grants**: un solo punto para obtener access tokens.
   - Grants no interactivos (`incoming-payment` y `quote`), en caché por servidor de autorización, que se vuelven a pedir al vencer.
   - El permiso permanente de Relia (`outgoing-payment` sobre su billetera, sin `receiver`, con `limits.debitAmount` y `limits.interval`).
   - La rotación del token (`token.rotate`) es single-flight: rotar revoca el token anterior, así que dos rotaciones concurrentes no pueden correr. Se rota cuando faltan menos de ~30 s para `expires_in` o tras un 401 del servidor de recursos, y siempre se guardan el `value` y el `manage` más recientes.
5. **Servicio de pagos**: crear, leer y cancelar, idempotencia y la máquina de estados del pago de riel. Es el único que cambia el estado de un pago. Cada cambio sube `version` y `updatedAt` en una sola escritura.
6. **Sincronizador**: un ciclo de fondo que avanza los pagos no finales y el permiso de Relia pendiente, consultando Open Payments. Toma su trabajo del almacén, así que retoma lo pendiente después de un reinicio. También hace cumplir `expiresAt` (ver abajo).
7. **Almacén**: SQLite en un solo archivo local, con tres tablas: pagos (incluye el estado interno del riel), registros de idempotencia y grants (el permiso de Relia y su historial). Los tokens y las continuaciones solo existen aquí; nunca salen en una respuesta ni en un log.

### Máquina de estados del pago de riel

Estados públicos: los del contrato (`pending_approval`, `processing`, `completed`, `failed`, `rejected`, `expired`, `cancelled`). Por dentro, cada pago guarda el **paso del riel** en el que va, para saber qué consultar.

**`outgoing`** (Relia paga). Todo ocurre dentro del `POST`:
1. Validar y resolver la contraparte: existe, no es Relia, tiene el mismo `assetCode` y `assetScale` que Relia y que `receiveAmount`.
2. Revisar que el permiso de Relia esté `active`. Si no, `503 relia_grant_unavailable`.
3. Pedir un grant no interactivo `incoming-payment` en el servidor de autorización del receptor y crear ahí el incoming payment con `incomingAmount = receiveAmount` y `metadata` con `idempotencyKey` y `description`.
4. Crear la quote en la billetera de Relia (`receiver` = el incoming payment).
5. Crear el outgoing payment en la billetera de Relia con `quoteId`, usando el token del permiso.
6. Guardar el pago en `processing` con `debitAmount` de la quote y las tres URLs. Responder `201`.

Si falla cualquier paso entre 3 y 5, no se guarda pago ni registro de idempotencia y se responde `503` (`relia_grant_unavailable` si el servidor rechazó el token o el tope; si no, `rail_unavailable`). Si alcanzó a crearse el incoming payment, se cierra con `incomingPayment.complete` para no dejar un cobro abierto. Si el riel rechazó el outgoing por exceder el tope, el permiso pasa a `exhausted`.

**`incoming`** (Relia cobra). En el `POST`:
1. Validar como arriba. `expiresAt` es obligatorio y debe ser futuro.
2. Crear el incoming payment en la billetera de Relia con `incomingAmount = receiveAmount`, `expiresAt` y `metadata`.
3. Pedir un grant no interactivo `quote` en el servidor de autorización del pagador y crear la quote en su billetera.
4. Pedir el grant **interactivo** `outgoing-payment` en el servidor de autorización del pagador: `identifier` = su billetera, `limits.debitAmount` = `debitAmount` de la quote, `limits.receiver` = el incoming payment, `interact.start: ["redirect"]` y **sin `finish`** (decisión del ticket 06: el pagador no vuelve a Relia y el proxy consulta).
5. Guardar `pending_approval` con `approval = { method: "redirect", url: interact.redirect }`, `debitAmount` de la quote y la continuación (URI, token y `wait`). Responder `201`.

En el sincronizador:
- Consultar la continuación (`grant.continue` con cuerpo vacío) sin bajar de `continue.wait`; un `too_fast` solo reprograma la consulta.
  - Respuesta con `access_token`: está aprobado. Crear el outgoing payment en la billetera del pagador con ese token y `quoteId`, y pasar a `processing`, con `approval` en null. Si la quote venció, cotizar de nuevo una vez; si la nueva quote supera el `debitAmount` aprobado, o vuelve a fallar, pasar a `failed` con `quote_expired`.
  - `401 request_denied` sin que el proxy haya cancelado: pasar a `rejected` y cerrar el incoming payment.
- Al llegar `expiresAt` en `pending_approval`: `grant.cancel` sobre la continuación, `incomingPayment.complete` y pasar a `expired`. El vencimiento es una fecha límite guardada que el sincronizador revisa en cada vuelta, no un `setTimeout`, para que sobreviva a un reinicio.

**Confirmación en `processing`** (ambas direcciones): leer el incoming payment y el outgoing payment.
- `completed` cuando el incoming payment tiene `completed: true` o `receivedAmount >= incomingAmount`. Se llenan `receivedAmount` y `completedAt`.
- `failed` cuando el outgoing payment tiene `failed: true`. El `failureCode` es `partial_payment` si `sentAmount > 0`, `insufficient_funds` si el riel lo dice de forma explícita y `rail_error` en los demás casos. El mensaje del riel va en `failureDetail`.
- En otro caso sigue en `processing` (no existe `unknown`). Se consulta cada ~1 s los primeros ~10 s, luego cada ~3 s y después de ~2 min cada ~30 s, sin fin.

**Cancelación** (`POST /payments/{id}/cancel`), con candado por pago para no cruzarse con el sincronizador:
- `incoming` en `pending_approval` y sin outgoing payment creado: `grant.cancel` (se ignora un `401`, que significa que ya estaba finalizado), `incomingPayment.complete` y pasar a `cancelled`. Si el riel no responde: `503 rail_unavailable` y el pago queda como estaba.
- Ya `cancelled`: `200` con el mismo pago, sin llamar al riel.
- Cualquier otro caso (`outgoing`, ya aprobado, `processing` o final): `409 payment_not_cancellable`.

### Idempotencia

- La clave es `Idempotency-Key` (UUID, el `pagos.id` del backend) más una huella del cuerpo normalizado.
- Misma clave y misma huella: `200` con el pago actual. Misma clave y otra huella: `409 idempotency_conflict`.
- Hay un candado en memoria por clave mientras dura la creación, así dos peticiones simultáneas producen un solo pago en el riel y la segunda recibe el resultado de la primera.
- Una creación que termina en `503` no deja registro, para que el reintento pueda crear el pago.
- El `id` del pago es un UUID propio del proxy (el `railPaymentId` del backend), distinto de la clave de idempotencia, que se devuelve como `idempotencyKey`.

### Permiso permanente de Relia (`/relia-grant`)

- `POST`: valida que `debitLimit` esté en la moneda y escala de Relia y que `interval` sea un intervalo ISO 8601 repetitivo. Pide el grant interactivo sobre la billetera de Relia sin `finish`, guarda la continuación y responde `202` con `awaiting_approval` y `approvalUrl`. El sincronizador consulta la continuación. Al aprobarse, guarda el token y el `manage`, pone `approvedAt` y el permiso nuevo reemplaza al anterior, que se revoca en el riel sin bloquear si falla.
- Estado que se informa: `awaiting_approval` si hay una petición sin aprobar, aunque exista un permiso activo anterior (así lo dice el contrato, y los `outgoing` responden `503` mientras tanto); `active` si hay un permiso aprobado y no agotado; `exhausted` si el riel rechazó un pago por el tope y no ha empezado el siguiente intervalo (se calcula del `interval`); `missing` en los demás casos. Una petición rechazada por el operador vuelve al estado anterior.
- Si al rotar el token el servidor dice que el grant está revocado, el permiso pasa a `missing`.

### Salud

- `GET /health` es público, responde con datos en memoria (la billetera de Relia que se resolvió al arrancar y el estado del permiso) y no llama al riel. `status: ok` solo si el permiso está `active`; si no, `degraded`. `rail: openPayments`.
- Rate limit sencillo por IP en esta ruta, con `429` en `problem+json`.

### Montos

- Todos los montos se manejan como cadenas de unidades mínimas y se comparan con `BigInt`, nunca con `Number`.
- Un pago solo admite el `assetCode` y el `assetScale` de la billetera de Relia (COP, escala 2 en la red de prueba). El proxy no convierte monedas.

## Testing Decisions

- **Sin pruebas unitarias ni suite automatizada** (decisión del equipo para el hackathon). El proxy se verifica por fuera, contra su contrato HTTP, que es el único seam. Un buen chequeo mira solo el comportamiento externo: código HTTP, `code` del error y forma del `Payment`. No mira cómo está hecho por dentro.
- **Prior art**: la colección [contracts/rail-proxy.collection.json](../../contracts/rail-proxy.collection.json) y el flujo sugerido del Rail Proxy en [contracts/README.md](../../contracts/README.md) §7 y [contracts/FLUJOS.md](../../contracts/FLUJOS.md). Se usan contra la red de prueba real con las billeteras del ticket 09 (`relia-test1`, `rosa-test` y `jeffdev`).
- **Checklist manual antes de dar por terminado** (cada punto con la colección de Postman):
  1. Arranque sin variables o con la llave equivocada: el proceso se niega a arrancar.
  2. `GET /health` sin key → `200 degraded` y `missing`. Cualquier otra ruta sin key → `401 unauthorized`.
  3. `POST /relia-grant` → `202` con `approvalUrl`; se aprueba en la wallet de prueba; `GET /relia-grant` pasa a `active` sin hacer nada más; `/health` dice `ok`. Reiniciar el proxy: sigue `active`.
  4. `GET /wallet-addresses` con un pointer real → `200 COP/2`; con uno inexistente → `404 wallet_not_found`; mal formado → `422`.
  5. `outgoing` a `jeffdev` por `"150000"` → `201 processing` → `completed` con `receivedAmount` = `receiveAmount`; el saldo se ve en la wallet de prueba.
  6. Repetir el mismo `POST` con la misma clave → `200` y el mismo `id`. Misma clave con otro monto → `409 idempotency_conflict`.
  7. `outgoing` a `relia-test1` → `422 same_wallet`. Con `assetCode: USD` → `422 unsupported_asset`.
  8. `incoming` desde `jeffdev` con `expiresAt` a +5 min → `201 pending_approval` con `approval.url`; aprobar → `processing` → `completed`.
  9. `incoming` y rechazar en la billetera → `rejected`.
  10. `incoming` con `expiresAt` a +1 min sin aprobar → `expired`; el enlace ya no deja aprobar.
  11. `incoming` y `cancel` → `200 cancelled`; repetir `cancel` → `200`; `cancel` de un `outgoing` → `409 payment_not_cancellable`.
  12. Reiniciar el proxy con un `incoming` en `pending_approval`: después de aprobar, el pago avanza igual.
  13. Pedir un permiso con tope pequeño y agotarlo: `outgoing` → `503 relia_grant_unavailable` y `GET /relia-grant` → `exhausted`.
  14. `version` sube en cada cambio de estado.
  15. Revisar los logs: no aparecen tokens, `manage`, continuaciones ni la llave.

## Out of Scope

- El backend .NET (traducir estados del pago a la operación, sus temporizadores de respaldo, el barrido de 15 s) y el frontend.
- Ganchos de demo o fallos forzados en el proxy: los simula el backend (ticket 08).
- Un adaptador Bre-B. El contrato lo permite, pero aquí solo se implementa Open Payments. `approval.method: qr` nunca se emite.
- Webhooks hacia el backend y `interact.finish` con redirect de vuelta (el contrato decidió consultar).
- Conversión entre monedas o pagos en una moneda distinta a la de la billetera de Relia.
- Varias instancias del proxy, alta disponibilidad, despliegue, contenedores y observabilidad más allá de logs.
- Interfaz para el permiso de Relia: lo maneja el operador con Postman o curl.
- Conciliación de saldos de la billetera de Relia contra el cupo (sigue "Not yet specified" en el mapa).
- Pruebas automatizadas (unitarias, de integración o de contrato).

## Further Notes

- **Sin verificar contra la red real** (de los research 01 y 02). El código debe leer estos valores de las respuestas, no asumirlos: `expires_in` del token, `continue.wait`, la vida de la quote (`expiresAt`), el error exacto al exceder el tope del permiso y si la red de prueba exige interacción para `quote` o `incoming-payment`. Lo que se observe en el checklist se anota en este ticket.
- **Experiencia del pagador sin `finish`**: después de aprobar, la wallet de prueba deja al pagador en una página en blanco del servidor de autorización. Es el costo aceptado de "no vuelve a ninguna página de Relia". Si el pagador cierra la pestaña antes de que cargue esa página, Rafiki no registra la aprobación y el pago termina en `expired` aunque haya aceptado. Hay que advertírselo a quien presenta.
- **Ventana del operador**: la interacción del permiso de Relia vence a los ~10 min en Rafiki. Si vence, se pide de nuevo con `POST /relia-grant`.
- **Antes de presentar**: el permiso de Relia debe quedar aprobado (requisito del ticket 10) y la billetera de Relia con fondos.
- **Llaves**: la wallet address de Relia publica su `jwks.json` con la developer key generada en la wallet de prueba. El key ID y la llave privada viven fuera del repo (ticket 09) y llegan al proxy solo por variables de entorno.
- **Tracker**: este repo todavía usa markdown local porque `gh` no tiene sesión. Cuando la tenga, este ticket se migra a GitHub Issues con la etiqueta `ready-for-agent`.
