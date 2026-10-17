# Relia Rail Proxy

Adaptador de riel de Relia sobre Open Payments. Implementa [contracts/rail-proxy.openapi.yaml](../contracts/rail-proxy.openapi.yaml): el backend .NET le pide pagar o cobrar a un payment pointer, y solo este proceso conoce GNAP, quotes, incoming y outgoing payments, y la llave de la billetera de Relia. Por qué existe: [ADR 0001](../docs/adr/0001-proxy-como-contrato-de-riel.md). Spec y tickets: [docs/issues/proxy-de-riel-node.md](../docs/issues/proxy-de-riel-node.md).

## Requisitos

- Node 22.12 o superior.
- La billetera de Relia en la wallet de prueba (`wallet.interledger-test.dev`) con una developer key generada. El key ID y el `private.key` se guardan fuera del repo.

## Configurar

```sh
cp .env.example .env
base64 -i ~/ruta/a/private.key | tr -d '\n'   # pegar el resultado en RELIA_PRIVATE_KEY_BASE64
```

| Variable | Obligatoria | Qué es |
|---|---|---|
| `RAIL_PROXY_API_KEY` | sí | Clave compartida con el backend. Toda ruta salvo `/health` exige `X-Api-Key` con este valor. |
| `RELIA_WALLET_ADDRESS` | sí | Payment pointer o URL https de la billetera de Relia. |
| `RELIA_KEY_ID` | sí | Key ID de la developer key. |
| `RELIA_PRIVATE_KEY_BASE64` | sí | El `private.key` (PEM Ed25519) en base64. |
| `HOST` / `PORT` | no | `0.0.0.0` / `4000`. |
| `LOG_LEVEL` | no | `info`. |
| `DATABASE_PATH` | no | Archivo SQLite con el estado del proxy. `data/rail-proxy.db`. |
| `HEALTH_RATE_LIMIT_PER_MINUTE` | no | Consultas por IP a `/health` antes de responder `429`. `60`. |

Al arrancar, el proxy resuelve la billetera de Relia y comprueba que publica la llave pública de `RELIA_PRIVATE_KEY_BASE64` con el `RELIA_KEY_ID` dado. Si falta una variable, la billetera no existe o la llave no corresponde, el proceso termina con un mensaje que nombra la causa.

## Correr

```sh
npm install
npm run dev         # recarga al guardar
# o
npm run build && npm start
```

Comprobar: `curl localhost:4000/health`. Responde `degraded` mientras el permiso permanente de Relia no esté activo.

Para probar el resto de rutas, usa la colección [contracts/rail-proxy.collection.json](../contracts/rail-proxy.collection.json) (ver [contracts/README.md](../contracts/README.md)).

## Permiso permanente de Relia (una vez, operador)

Sin él, los pagos `outgoing` (depósitos y pagos de comisión) responden `503 relia_grant_unavailable`.

```sh
curl -X POST localhost:4000/relia-grant -H "X-Api-Key: $RAIL_PROXY_API_KEY" -H 'Content-Type: application/json' \
  -d '{"debitLimit":{"value":"100000000","assetCode":"COP","assetScale":2},"interval":"R/2026-10-16T00:00:00-05:00/P1D"}'
```

1. Abre la `approvalUrl` de la respuesta, inicia sesión con la cuenta de Relia en la wallet de prueba y aprueba. Tienes ~10 minutos.
2. Después de aprobar, la wallet queda en una página en blanco: es normal. El proxy detecta la aprobación solo.
3. `GET /relia-grant` pasa a `active` y `/health` a `ok`. El permiso queda en `DATABASE_PATH` y sobrevive a los reinicios.

El inicio del `interval` debe estar en el pasado: el riel rechaza los pagos creados antes de que empiece (el proxy responde `422`). El token del permiso dura poco (~10 min); el proxy lo rota solo antes de que venza.

Estados que verás en `GET /relia-grant`:
- `exhausted`: el riel rechazó un pago por el tope del periodo. Los pagos se siguen intentando (uno más pequeño puede caber) y vuelve a `active` con el primer pago que pase o al empezar el siguiente periodo.
- `missing` después de haber estado `active`: el riel revocó el permiso (por ejemplo, lo revocaste desde la wallet). Pide uno nuevo.

Para subir el tope, pide uno nuevo: al aprobarlo reemplaza al anterior, que se revoca en el riel. Mientras el nuevo no se apruebe, el estado es `awaiting_approval` y los `outgoing` responden `503`. Si el operador lo rechaza o se vence la ventana, vuelve a regir el anterior.

## Módulo Open Payments (SDK)

El proxy usa `@interledger/open-payments` 7.4.0. Es el único sitio de Relia que lo usa. Hay un solo cliente autenticado, creado al arrancar con la developer key de Relia (`RELIA_WALLET_ADDRESS`, `RELIA_KEY_ID`, `RELIA_PRIVATE_KEY_BASE64`). El SDK firma cada petición (HTTP message signatures Ed25519) y manda los tokens GNAP.

| Archivo | Qué hace con el SDK |
|---|---|
| [src/main.ts](src/main.ts) | `createAuthenticatedClient` y arranque. |
| [src/wallets.ts](src/wallets.ts) | `walletAddress.get` (resolver pointers, con caché de 5 min) y `walletAddress.getKeys` (validar la llave al arrancar). |
| [src/grants.ts](src/grants.ts) | Grants no interactivos `incoming-payment` y `quote`, en caché en memoria. |
| [src/relia-grant.ts](src/relia-grant.ts) | Permiso permanente de Relia: grant `outgoing-payment` interactivo con tope por intervalo, sondeo, rotación y revocación. |
| [src/payments.ts](src/payments.ts) | Incoming payments, quotes y outgoing payments. También la aprobación del pagador y la confirmación. |
| [src/synchronizer.ts](src/synchronizer.ts) | Vuelta de fondo (~1 s) que sondea grants y pagos. |

**Relia paga** (`outgoing`: depósito, comisión). Nace en `processing` sin aprobación humana.
1. `walletAddress.get` del receptor.
2. `incomingPayment.create` en el receptor, por `receiveAmount`.
3. `quote.create` en la billetera de Relia.
4. `outgoingPayment.create` en la billetera de Relia, con el token del permiso permanente.

**Relia cobra** (`incoming`: retiro, carga de cupo):
1. `incomingPayment.create` en la billetera de Relia, con `expiresAt`.
2. `quote.create` en la billetera del pagador.
3. `grant.request` interactivo `outgoing-payment` en el pagador, limitado a esa quote y ese incoming. El `interact.redirect` es la `approval.url`.
4. El sincronizador hace `grant.continue` hasta saber si el pagador aprobó o rechazó.
5. Si aprobó, el sincronizador hace `outgoingPayment.create` en la billetera del pagador con su token.

**Confirmación:** el sincronizador lee `incomingPayment.get`. El pago queda `completed` cuando llegó el monto completo. Lee `outgoingPayment.get` para detectar `failed`.

**Al cancelar, vencer o fallar:** `grant.cancel` revoca el permiso e `incomingPayment.complete` cierra el cobro. Los tokens se rotan con `token.rotate` antes de vencer o ante un 401.

Los grants interactivos se piden sin `interact.finish`: no hay URL de retorno, el proxy sondea la continuación. Por eso la wallet queda en blanco después de aprobar.

Detalle, citas a la documentación oficial y diferencias conocidas: [docs/research/open-payments-sdk.md](../docs/research/open-payments-sdk.md).

## Errores

Todo error sale en `application/problem+json` con un `code` del enum `ErrorCode` del contrato. Programa contra el `code`, no contra el texto.
