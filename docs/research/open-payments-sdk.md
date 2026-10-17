# Open Payments SDK en el proxy de riel: cómo y por qué se usa

Investigado el 2026-10-16. Fuentes: el código de `rail-proxy/` (citas `ruta:línea`, relativas a la raíz del repo), la documentación oficial en [openpayments.dev](https://openpayments.dev), el README del SDK en GitHub ([interledger/open-payments-node, `packages/open-payments`](https://github.com/interledger/open-payments-node/tree/main/packages/open-payments)) y el SDK instalado en `rail-proxy/node_modules/@interledger/open-payments` (v7.4.0).

## Respuesta corta

- El proxy usa `@interledger/open-payments` **v7.4.0** (`rail-proxy/package.json:17` pide `^7.4.0`; `rail-proxy/package-lock.json:698-700` fija `7.4.0`).
- Es el **único** componente de Relia que habla Open Payments. El backend .NET solo pide "pagar a un pointer" o "cobrar a un pointer" con un contrato agnóstico del riel ([ADR 0001](../adr/0001-proxy-como-contrato-de-riel.md)). Todo lo de GNAP, quotes, incoming y outgoing payments, y la llave de la billetera de Relia, vive aquí (`rail-proxy/README.md:3`).
- Se crea **un solo cliente autenticado** al arrancar, firmado con la developer key Ed25519 de la billetera de Relia (`rail-proxy/src/main.ts:16-25`). Todas las llamadas usan ese cliente.
- Hay tres tipos de grant:
  1. `incoming-payment` y `quote`: **no interactivos**. Se piden al vuelo y se cachean en memoria (`rail-proxy/src/grants.ts`).
  2. `outgoing-payment` de Relia: **interactivo, una sola vez**, con tope por intervalo. Es el "permiso permanente" que aprueba un operador (`rail-proxy/src/relia-grant.ts`).
  3. `outgoing-payment` del pagador: **interactivo, uno por cobro**. Lo aprueba el usuario en su billetera (`rail-proxy/src/payments.ts:494-509`).
- No hay callback de redirección: el proxy pide los grants interactivos **sin `interact.finish`** y **sondea** la continuación GNAP con un sincronizador de fondo (`rail-proxy/src/synchronizer.ts`). El estado de los pagos se confirma también por sondeo.

## 1. Configuración y cliente autenticado

| Variable | Para qué la usa el SDK | Dónde |
|---|---|---|
| `RELIA_WALLET_ADDRESS` | `walletAddressUrl`: la identidad del cliente. Se acepta un payment pointer (`$host/ruta`) y se convierte a URL https. | `rail-proxy/src/config.ts:36`, `rail-proxy/src/wallets.ts:19-39`, `rail-proxy/src/main.ts:17` |
| `RELIA_KEY_ID` | `keyId` de la firma HTTP. | `rail-proxy/src/config.ts:37`, `rail-proxy/src/main.ts:19` |
| `RELIA_PRIVATE_KEY_BASE64` | `privateKey`: el `private.key` PEM de la wallet de prueba, en base64. Se valida que sea Ed25519. | `rail-proxy/src/config.ts:54-70`, `rail-proxy/src/main.ts:18` |
| `RAIL_PROXY_API_KEY` | No es del SDK. Protege las rutas del proxy (`X-Api-Key`). | `rail-proxy/src/config.ts:23,35` |

```ts
// rail-proxy/src/main.ts:16-25
const client = await createAuthenticatedClient({
  walletAddressUrl: toWalletUrl(config.reliaWalletAddress),
  privateKey: config.reliaPrivateKey,
  keyId: config.reliaKeyId,
  logLevel: 'silent',
  requestTimeoutMs: 10_000,
});
```

**Por qué:** en Open Payments el cliente se identifica con una wallet address y un par de llaves. El servidor de autorización busca la llave pública en `WALLET_ADDRESS/jwks.json` por el `keyId` del header `Signature-Input` y valida la firma ([Client keys](https://openpayments.dev/identity/client-keys/)). Las firmas usan Ed25519 ([HTTP signatures](https://openpayments.dev/identity/http-signatures/)). El README del SDK describe los tres argumentos y exige el JWKS en `{walletAddressUrl}/jwks.json`.

**Qué hace el SDK por dentro (v7.4.0):**
- Firma toda petición POST o con `Authorization`: añade `Signature` y `Signature-Input`, y `Content-Digest` cuando hay cuerpo (`node_modules/@interledger/open-payments/dist/client/requests.js:227-252`).
- Manda los tokens como `Authorization: GNAP <token>` (mismo archivo, líneas 16, 55, 81).
- Rellena `client: walletAddressUrl` en cada grant request (`dist/client/index.js:197`). Por eso el proxy nunca lo pasa a mano.
- `requestTimeoutMs` por defecto es 5000 ms (`dist/config.js:4`). El proxy lo sube a 10 s para que una petición colgada no frene el sincronizador (`rail-proxy/src/main.ts:23-24`).
- `logLevel` por defecto ya es `silent` (`dist/client/index.js:67`). El proxy lo fija explícito porque registra los errores del riel él mismo con `describeRailError` (`rail-proxy/src/main.ts:20-22`, `rail-proxy/src/problem.ts`).

**Comprobación al arrancar:** el proxy resuelve la billetera de Relia y llama a `client.walletAddress.getKeys` para confirmar que el `RELIA_KEY_ID` está publicado y que su `x` coincide con la llave pública derivada de la privada (`rail-proxy/src/wallets.ts:112-127`, `rail-proxy/src/main.ts:28-31`). **Por qué:** sin esto, un key ID o una llave equivocados solo fallarían en el primer pago, con un 401 poco claro.

## 2. Resolver billeteras (`walletAddress.get`)

`WalletResolver.resolve` convierte pointer → URL, llama a `client.walletAddress.get({ url })` y guarda `id`, `assetCode`, `assetScale`, `authServer` y `resourceServer` en caché 5 minutos (`rail-proxy/src/wallets.ts:47-77`).

**Por qué:**
- Cada paso siguiente necesita el `authServer` (para pedir grants) y el `resourceServer` (para crear recursos) de la contraparte. El primer paso del flujo oficial es justo este GET público ([Open Payments flow](https://openpayments.dev/concepts/op-flow/)).
- Sirve para validar antes de mover dinero. Se rechaza pagar a la propia billetera de Relia (`same_wallet`) o a una de otra moneda o escala (`unsupported_asset`) (`rail-proxy/src/payments.ts:324-344`).
- Expone `GET /wallet-addresses` para que el backend confirme un pointer (`rail-proxy/src/app.ts:123-138`).

Los errores se clasifican: 4xx → `404 wallet_not_found`, 5xx/429 → `503 rail_unavailable`. Un `fetch failed` se desambigua con una consulta DNS, porque el SDK no distingue "dominio inexistente" de "riel caído" (`rail-proxy/src/wallets.ts:79-109`).

## 3. Grants no interactivos (`incoming-payment`, `quote`)

`NonInteractiveGrants` pide `grant.request({ url: authServer }, { access_token: { access: [...] } })` con:
- `incoming-payment`: `create`, `read`, `complete`.
- `quote`: `create`, `read`.

(`rail-proxy/src/grants.ts:5-8,52-76`.)

- Cachea el token por `authServer|tipo` y lo renueva 30 s antes de `expires_in`.
- Deduplica las peticiones en vuelo.
- Si el servidor de recursos responde 401, descarta el token y reintenta una vez (`rail-proxy/src/grants.ts:27-50`).
- Si el AS pide interacción, lo trata como error (`rail-proxy/src/grants.ts:65-69`).

**Por qué:** la documentación dice que "Incoming payment grants are non-interactive by default" y "Quote grants are non-interactive by default" ([Grants y auth](https://openpayments.dev/concepts/auth/)). Pedirlos es barato, así que no se persisten (`rail-proxy/src/grants.ts:13-16`).

## 4. Permiso permanente de Relia (grant `outgoing-payment` interactivo, una vez)

`POST /relia-grant` (`rail-proxy/src/app.ts:162-180`) llama a `ReliaGrantService.request` (`rail-proxy/src/relia-grant.ts:212-301`). Esa función:

1. Valida que la moneda coincida, que el tope sea mayor que 0 y que el `interval` ISO 8601 repetitivo empiece en el pasado.
2. Pide:
   ```ts
   // rail-proxy/src/relia-grant.ts:245-260
   client.grant.request({ url: wallet.authServer }, {
     access_token: { access: [{
       type: 'outgoing-payment', actions: ['create', 'read', 'list'],
       identifier: wallet.url,
       limits: { debitAmount: debitLimit, interval },
     }] },
     interact: { start: ['redirect'] },   // sin finish
   })
   ```
3. Guarda `interact.redirect` (la `approvalUrl` que abre el operador), `continue.uri`, `continue.access_token` y el próximo sondeo según `continue.wait` (`rail-proxy/src/relia-grant.ts:270-296`).

El sincronizador corre cada ~1 s (`rail-proxy/src/synchronizer.ts:19`) y llama a `client.grant.continue({ url, accessToken })` **sin `interact_ref`** (`rail-proxy/src/relia-grant.ts:309-412`):

| Resultado | Qué hace el proxy |
|---|---|
| Sigue pendiente (sin `access_token`) | Guarda la continuación nueva y respeta `wait`. |
| Error `request_denied` | Pasa a `rejected`. |
| Error `too_fast` | Reintenta en 5 s. |
| Aprobado | Guarda `access_token.value`, `access_token.manage` y `expires_in`, más la continuación (para poder revocar). Pasa a `approved` y revoca con `grant.cancel` el permiso anterior. |
| Pasan ~11 min sin aprobación | Se abandona y se revoca (`rail-proxy/src/relia-grant.ts:53-54,318-324`). |

**Rotación:** `withPermitToken` rota con `client.token.rotate({ url: manage_url, accessToken })` 30 s antes de que venza el token, o al recibir un 401. Si la rotación da 404, el permiso se marca `revoked` (`rail-proxy/src/relia-grant.ts:109-197`).

**Por qué:**
- Open Payments exige que todo grant `outgoing-payment` sea interactivo ("Open Payments requires outgoing payment grant requests to be interactive", [auth](https://openpayments.dev/concepts/auth/)).
- Los depósitos y la comisión del corresponsal deben salir **sin que nadie apruebe cada pago** (historias 1-3 en [docs/issues/proxy-de-riel-node.md](../issues/proxy-de-riel-node.md)).
- La solución oficial es un grant "para pagos futuros": sin `receiver`, con `debitAmount` e `interval` repetitivo, que se aprueba una vez ([Outgoing grant for future payments](https://openpayments.dev/guides/outgoing-grant-future-payments/)).
- La rotación está documentada: "The new token replaces the existing token and has the same rights and properties" ([token-rotate](https://openpayments.dev/sdk/token-rotate/)).
- La revocación usa la continuación: `client.grant.cancel({ accessToken: CONTINUE_ACCESS_TOKEN, url: CONTINUE_URI })` ([grant-revoke](https://openpayments.dev/sdk/grant-revoke/)).

## 5. Relia paga (`direction: outgoing`): depósito o pago de comisión

`PaymentService.createOutgoing` (`rail-proxy/src/payments.ts:350-453`):

1. Llama a `assertCanPay()`: falla con `503 relia_grant_unavailable` antes de abrir nada en el riel (`:357`).
2. `incomingPayment.create` en el **resource server del receptor**, con `incomingAmount = receiveAmount` y `metadata.externalRef = Idempotency-Key`. Usa un token `incoming-payment` del AS del receptor (`:360-370`).
3. `quote.create` en el **resource server de Relia** con `{ walletAddress: relia, receiver: incoming.id, method: 'ilp' }`. Usa un token `quote` del AS de Relia (`:375-387`). De la quote se toma `debitAmount`, lo que sale de Relia.
4. `outgoingPayment.create` en el RS de Relia con `{ walletAddress, quoteId, metadata }`, usando el token del **permiso permanente** (`:389-396`).
5. Si el paso 3 o el 4 fallan, cierra el incoming con `incomingPayment.complete` (`:385,399`, `:894-903`). Errores del paso 4:
   - 403: es el tope. Marca el permiso `exhausted` y responde `503 relia_grant_unavailable` (`:401-411`).
   - 401 persistente: responde `503` (`:412-419`).
6. El pago nace en `processing` (`:428`).

**Por qué:**
- Es el flujo oficial: incoming payment en el receptor, quote en el emisor, outgoing payment con el token del grant interactivo ([Open Payments flow](https://openpayments.dev/concepts/op-flow/)).
- Se usa quote con `incomingAmount` fijo para que el usuario reciba **exactamente** el neto del depósito (historia 1 de la spec).
- Cerrar el incoming cuando algo falla evita dejar un cobro abierto en la billetera del receptor ("no further payments will be sent", [incoming-complete](https://openpayments.dev/sdk/incoming-complete/)).

## 6. Relia cobra (`direction: incoming`): retiro o carga de cupo

`PaymentService.createIncoming` (`rail-proxy/src/payments.ts:460-557`):

1. `incomingPayment.create` en el RS de **Relia**, con `expiresAt` (obligatorio en este sentido) (`:468-483`).
2. `quote.create` en el RS del **pagador** (`:488-493`).
3. `grant.request` interactivo `outgoing-payment` en el AS del **pagador**, con `limits: { debitAmount: quote.debitAmount, receiver: incoming.id }`, `interact.start: ['redirect']` y sin `finish` (`:494-509`).
4. El pago nace en `pending_approval`. La `approval.url` es el `interact.redirect`, que el backend muestra al usuario (`:547`, `:973`).

El sincronizador (`rail-proxy/src/payments.ts:215-242`) luego:
- **Sondea la aprobación** con `grant.continue` (`pollApproval`, `:563-626`). Con `request_denied` pasa a `rejected` y cierra el incoming. Al aprobarse guarda el token del pagador y pasa a `processing`.
- **Crea el outgoing en la billetera del pagador** con su token (`createPayerOutgoing`, `:634-697`):
  - Si la quote venció (400 "invalid quote"), recotiza **una vez**. Si la nueva quote supera el débito aprobado, falla con `quote_expired`.
  - Antes de reintentar busca con `outgoingPayment.list` si el intento anterior sí creó el pago, para no debitar dos veces (`:703-720`).
  - Rota el token del pagador con `token.rotate` si recibe 401 (`:723-738`).
- **Vence o cancela**: si pasa `expiresAt` sin aprobación, o el backend llama `POST /payments/{id}/cancel`, revoca con `grant.cancel` y cierra el incoming (`:248-307`).

**Por qué:** cobrar de la billetera de otro requiere su consentimiento explícito, siempre interactivo ([auth](https://openpayments.dev/concepts/auth/)). `limits.receiver` ata el permiso a este único cobro, así el pagador aprueba solo ese monto hacia ese destino. Sin `finish` el proxy no necesita exponer una URL de retorno. La documentación contempla el sondeo cuando no hay interfaz: "consider implementing a polling mechanism to check that the interaction has completed" ([grant-continue](https://openpayments.dev/sdk/grant-continue/)). En el SDK, el segundo argumento de `grant.continue` (el que lleva `interact_ref`) es opcional (`dist/client/grant.d.ts`: `continue(postArgs, args?)`).

## 7. Confirmar el estado (sondeo de `incomingPayment.get` / `outgoingPayment.get`)

`confirm` (`rail-proxy/src/payments.ts:809-866`) decide así:
- **Éxito:** lee primero el incoming (`incomingPayment.get`). Si `receivedAmount >= receiveAmount`, el pago pasa a `completed` y se revoca el permiso del pagador.
- **Fallo:** lee el outgoing (`outgoingPayment.get`, con el token del permiso de Relia o del pagador según el sentido). Si `failed: true`, pasa a `failed`: `partial_payment` si `sentAmount > 0`, si no `rail_error`.
- **Frecuencia:** se reconsulta cada ~1 s los primeros 10 s, cada ~3 s hasta 2 min y luego cada ~30 s (`:953-958`).

**Por qué:** el esquema del SDK no tiene un campo `state` en el outgoing payment, solo `failed` y `sentAmount` (`dist/openapi/generated/resource-server-types.d.ts:314-325`). El incoming payment sí dice cuándo recibió todo: "If `incomingAmount` is specified and the total received … equals or exceeds the specified `incomingAmount` … set `completed` to `true`" (mismo archivo, línea 29). Por eso el éxito se mide en el lado que recibe.

## Mapa de llamadas al SDK

| Llamada SDK | Archivo:línea | Propósito en Relia |
|---|---|---|
| `createAuthenticatedClient` | `rail-proxy/src/main.ts:16` | Identidad firmada de Relia en el riel. |
| `walletAddress.get` | `rail-proxy/src/wallets.ts:62` | Resolver pointers (auth/resource server, moneda). |
| `walletAddress.getKeys` | `rail-proxy/src/wallets.ts:118` | Validar la llave al arrancar. |
| `grant.request` (no interactivo) | `rail-proxy/src/grants.ts:53` | Tokens `incoming-payment` y `quote`. |
| `grant.request` (interactivo, Relia) | `rail-proxy/src/relia-grant.ts:245` | Permiso permanente con tope por intervalo. |
| `grant.request` (interactivo, pagador) | `rail-proxy/src/payments.ts:494` | Aprobación de un cobro puntual. |
| `grant.continue` | `rail-proxy/src/relia-grant.ts:328`, `rail-proxy/src/payments.ts:571` | Sondeo de la aprobación. |
| `grant.cancel` | `rail-proxy/src/relia-grant.ts:422`, `rail-proxy/src/payments.ts:301` | Revocar permisos reemplazados, vencidos, cancelados o terminados. |
| `token.rotate` | `rail-proxy/src/relia-grant.ts:170`, `rail-proxy/src/payments.ts:729` | Renovar tokens de vida corta. |
| `incomingPayment.create` | `rail-proxy/src/payments.ts:363,471` | Destino del dinero. |
| `incomingPayment.get` | `rail-proxy/src/payments.ts:818` | Confirmar que llegó todo. |
| `incomingPayment.complete` | `rail-proxy/src/payments.ts:898` | Cerrar cobros que no seguirán. |
| `quote.create` | `rail-proxy/src/payments.ts:377,489,671` | Fijar lo que se debita. |
| `outgoingPayment.create` | `rail-proxy/src/payments.ts:392,639` | Mover el dinero. |
| `outgoingPayment.get` | `rail-proxy/src/payments.ts:874,883` | Detectar fallos. |
| `outgoingPayment.list` | `rail-proxy/src/payments.ts:706` | Evitar el doble débito tras una respuesta perdida. |

Helpers de tipo que usa el proxy: `isPendingGrant` (en v7.4.0 solo comprueba `!!grant.interact`) e `isFinalizedGrantWithAccessToken` (solo comprueba `!!grant.access_token`) (`dist/types.js:10-18`). Para distinguir errores usa `OpenPaymentsClientError` (`status`, `code`, `description`).

## Cómo probarlo (Postman)

No hay tests unitarios. Se usa la colección [contracts/rail-proxy.collection.json](../../contracts/rail-proxy.collection.json) (guía en [contracts/README.md](../../contracts/README.md)):

1. `health / Estado del proxy, del riel y del permiso de Relia`: muestra `degraded` mientras no haya permiso activo.
2. `relia-grant / Pedir un nuevo permiso permanente…`: devuelve `approvalUrl`. Se aprueba en la wallet de prueba y luego `relia-grant / Estado del permiso permanente de Relia` debe pasar a `active`.
3. `wallet-addresses / Resolver un payment pointer`: ejercita `walletAddress.get`.
4. `payments / Crear un pago de riel`:
   - con `outgoing`: incoming, quote y outgoing de una vez.
   - con `incoming`: devuelve `approval.url`, que el pagador aprueba.
5. `payments/{paymentId} / Consultar un pago de riel`: muestra el estado que deja el sincronizador.
6. `payments/{paymentId}/cancel / Cancelar un cobro pendiente de aprobación`: ejercita `grant.cancel` e `incomingPayment.complete`.

## Incertidumbres y diferencias con la documentación oficial

1. **Sin `interact.finish` y sin verificación de hash.** Las guías oficiales muestran la interacción con `finish` (redirect + `nonce`), luego `grant.continue` con `interact_ref` y la verificación del `hash` ([op-flow](https://openpayments.dev/concepts/op-flow/), [grant-continue](https://openpayments.dev/sdk/grant-continue/)). El proxy omite `finish` y sondea. La documentación lo menciona como alternativa sin interfaz y el SDK lo permite, pero no hay una guía oficial paso a paso de ese modo. El proxy depende de cómo Rafiki responde al sondeo: `wait`, `too_fast`, `request_denied`. Consecuencia que el README ya advierte: tras aprobar, la wallet queda en una página en blanco (`rail-proxy/README.md:53`).
2. **Constantes de Rafiki no verificadas aquí contra la fuente:**
   - Ventana de aprobación de 10 min (`INTERACTION_EXPIRY_SECONDS`) (`rail-proxy/src/relia-grant.ts:53`).
   - 403 si el pago se crea antes del inicio del `interval` (`:232`).
   - 404 `invalid_rotation` si el grant fue revocado (`:172-173`).
   - 400 "invalid quote" cuando la quote venció (`rail-proxy/src/payments.ts:989-992`).

   Son comportamiento de la implementación (Rafiki / test wallet), no del estándar. `isInvalidQuote` detecta el error buscando "quote" en el texto, lo cual es frágil.
3. **403 = tope agotado.** El proxy asume que todo 403 al crear un outgoing con el permiso de Relia es por el tope (`rail-proxy/src/payments.ts:401-404`). La documentación no define ese código de error. Otro 403 (por ejemplo, un permiso mal configurado) se reportaría como `exhausted`.
4. **`outgoingPayment.list` solo lee la primera página** (`rail-proxy/src/payments.ts:707`). En una billetera con muchos pagos, el control de doble débito podría no encontrar el pago anterior. El propio código lo marca como no verificado.
5. **Éxito por `receivedAmount` en vez de `completed`.** Es equivalente según la spec (línea 29 del tipo generado), pero el proxy no lee `completed`.
6. **Repositorio del SDK.** El `package.json` del paquete apunta a `github.com/interledger/open-payments` (`directory: packages/open-payments`), pero su `homepage` y el README actual están en `interledger/open-payments-node`. El código del SDK se mudó a ese repo; las citas de este documento son al paquete instalado (v7.4.0).
7. **Rutas antiguas de la documentación.** Varias URLs de guías (por ejemplo `/guides/create-interactive-grant/`) ya dan 404. Las citas usan las rutas vigentes del sitemap de openpayments.dev a la fecha.
