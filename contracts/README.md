# Contratos y colecciones de Postman

En esta carpeta están los dos contratos OpenAPI de Relia y las colecciones de Postman que salen de ellos.

| Contrato (fuente de verdad) | Colección de Postman | Quién llama a quién | URL local | Autenticación |
|---|---|---|---|---|
| [relia-api.openapi.yaml](relia-api.openapi.yaml) | [relia-api.collection.json](relia-api.collection.json) | Frontend → backend .NET | `http://localhost:5080` | `Authorization: Bearer <JWT>` |
| [rail-proxy.openapi.yaml](rail-proxy.openapi.yaml) | [rail-proxy.collection.json](rail-proxy.collection.json) | Backend .NET → proxy Node (Open Payments) | `http://localhost:4000` | `X-Api-Key: <clave compartida>` |

**El YAML manda.** Las colecciones se generan desde el YAML y no se editan a mano. Si algo no cuadra, se corrige el YAML y se regenera la colección (ver [Regenerar las colecciones](#regenerar-las-colecciones)).

Por qué hay un proxy aparte: [ADR 0001](../docs/adr/0001-proxy-como-contrato-de-riel.md).

---

## 1. Importar en Postman

1. En Postman: **Import** → arrastra los dos archivos `*.collection.json`.
2. Quedan dos colecciones: **Relia API** y **Relia Rail Proxy**. Las carpetas siguen las rutas (`agents`, `operations`, `payments/{paymentId}/cancel`, …).
3. Cada request trae su descripción (tomada del contrato) y, en la pestaña **Examples**, una respuesta de ejemplo por cada código posible (200, 401, 404, 409, 422, 503…).

## 2. Crear un environment

Las colecciones traen `baseUrl`, pero **no** traen los secretos. Crea un environment (por ejemplo `Relia local`) con estas variables:

| Variable | Usada por | Valor |
|---|---|---|
| `baseUrl` | ambas | Ya viene en cada colección (`5080` API, `4000` proxy). Defínela en el environment solo si apuntas a otro host. Ojo: una variable de environment con el mismo nombre pisa la de la colección en **las dos** colecciones; si las usas a la vez, deja `baseUrl` solo en las colecciones. |
| `bearerToken` | Relia API | El `accessToken` que devuelve `POST /auth/login`. Ver el paso 3. |
| `apiKey` | Rail Proxy | La clave compartida entre backend y proxy (la misma que tiene el proxy en su variable de entorno). |

Selecciona el environment arriba a la derecha antes de enviar requests.

## 3. Relia API: iniciar sesión

La colección usa auth **Bearer Token = `{{bearerToken}}`** a nivel de colección; todas las requests la heredan.

1. Abre `auth / Iniciar sesión como corresponsal o admin` y pon un `email` y `password` reales.
2. Para no copiar el token a mano, pega esto en la pestaña **Scripts → Post-response** de esa request:

   ```js
   const body = pm.response.json();
   if (pm.response.code === 200) {
     pm.environment.set("bearerToken", body.accessToken);
     if (body.agentId) pm.environment.set("agentId", body.agentId);
   }
   ```

   Así cada login deja listos `bearerToken` y `agentId`. Ese script se pierde al reimportar la colección regenerada; guárdalo también en tu environment o en un fork.
3. El rol sale del token: `agent` solo opera sobre su propio corresponsal; `admin` lee todo y crea liquidaciones, pero recibe 403 en las rutas que operan por el corresponsal (depósitos, retiros, cargas, ajustes, cancelar, confirmar entrega).

**Rutas `/demo/*`:** no piden login y solo existen con `DEMO_MODE=true` en el backend. Sin ese flag responden 404.

## 4. Rail Proxy: la API key

La colección usa auth **API Key**, header `X-Api-Key`, valor `{{apiKey}}`. Con la variable `apiKey` en el environment basta. Sin ella, todo responde 401 `unauthorized`.

Para revisar que el proxy está vivo: `health / Estado del proxy, del riel y del permiso de Relia`. Si `reliaGrant` no está activo, los pagos `outgoing` van a responder 503 `relia_grant_unavailable`; pide uno con `POST /relia-grant` y apruébalo en la `approvalUrl`.

## 5. Antes de enviar: cambia los valores de ejemplo

El generador rellena parámetros y cuerpos con datos falsos sacados del esquema. **Hay que cambiarlos todos**:

| Lo que ves | Qué poner |
|---|---|
| `<uuid>` en `:agentId`, `:operationId`, `:paymentId`, `Idempotency-Key` | Un id real (por ejemplo `{{agentId}}`). Para `Idempotency-Key` usa `{{$guid}}` y Postman genera uno nuevo en cada envío. |
| `<string>`, `<integer>`, `<dateTime>` | El valor real. En el proxy, `assetCode`/`assetScale` en la red de prueba son `COP` y `2`. |
| Payment pointers raros como `$=*rh5j` o `$aa4m5Q\|` | Un payment pointer real de la wallet de prueba, por ejemplo `$ilp.interledger-test.dev/alguien`. |
| `"value": "388400"` en montos del proxy | Monto en unidades mínimas: `"150000"` con escala 2 = 1.500,00 COP. |

Los path params (`:agentId`, …) se editan en la pestaña **Params → Path Variables**.

## 6. Convenciones del contrato

- **Idempotencia:** toda creación lleva `Idempotency-Key`. Repetir la misma clave con el mismo cuerpo devuelve el recurso ya creado (200 en vez de 201). La misma clave con otro cuerpo da 409 `idempotency_conflict`. En el proxy, la clave es el id del pago en el backend (`pagos.id`).
- **Asincronía:** las operaciones de la API se crean en `pending`; se consulta `GET /operations/{operationId}` cada 1–2 s hasta un estado final. En el proxy no hay webhooks: se consulta `GET /payments/{paymentId}`.
- **Dinero:** en la API, enteros en COP sin decimales. En el proxy, `{ value, assetCode, assetScale }` en unidades mínimas del riel.
- **Errores:** `application/problem+json` (RFC 9457) con un `code` estable (`validation_error`, `not_found`, `rail_unavailable`, …). Programa contra el `code`, no contra el texto.

Nota: en los ejemplos de error de la colección del proxy el campo `code` siempre dice `same_wallet`. Es un efecto del generador (toma el primer valor del enum); el código real depende del caso y está en el nombre de cada ejemplo.

## 7. Flujo de prueba sugerido

Qué endpoints usar en cada flujo (depositar, retirar, cargar cupo, vincular, admin, demo): [FLUJOS.md](FLUJOS.md). Abajo, la versión corta.

**Relia API (corresponsal):**
1. `POST /auth/login` → guarda token y `agentId`.
2. `GET /agents/{agentId}` → saldos.
3. `GET /keys/{phone}` → valida la llave del usuario.
4. `POST /agents/{agentId}/operations/preview` → desglose.
5. `POST /agents/{agentId}/deposits` (con `Idempotency-Key: {{$guid}}`) → copia el `id`.
6. `GET /operations/{operationId}` hasta estado final → `GET /operations/{operationId}/receipt`.

**Rail Proxy:**
1. `GET /health` y `GET /relia-grant`.
2. `GET /wallet-addresses?pointer=...` → confirma la billetera.
3. `POST /payments` con `direction: incoming` → abre `approval.url` y aprueba en la wallet de prueba.
4. `GET /payments/{paymentId}` hasta un estado final (`completed`, `failed`, `rejected`, `expired`, `cancelled`), o `POST /payments/{paymentId}/cancel` mientras siga en `pending_approval`.

**Pagos entre monedas:** en `payments / Crear un pago de riel`, pestaña **Examples**, están los cuerpos de cada caso
(`outgoingCop`, `outgoingOtraMoneda`, `incomingCop`, `incomingOtraMoneda`, `sinRutaDeConversion`). Cambia los payment
pointers por billeteras de prueba en COP, en USD/EUR y en una moneda sin ruta. Qué revisar:
- `outgoing` a otra moneda: `completed`, `debitAmount` en COP igual a `receiveAmount`, `quotedReceiveAmount` y
  `receivedAmount` en la moneda del receptor, y su incoming payment cerrado.
- `incoming` desde otra moneda: `pending_approval`, aprobar en la billetera, `completed`, `receivedAmount` en COP igual
  a `receiveAmount` y `debitAmount` en la moneda del pagador.
- Sin ruta: 422 `unsupported_asset` y ningún incoming payment abierto.

## Regenerar las colecciones

Después de cambiar un YAML, valida y regenera desde la raíz del repo:

```sh
# 1. Lint de los contratos
npx @redocly/cli@1.25.11 lint --config contracts/redocly.yaml

# 2. Regenerar las colecciones (carpetas por ruta)
npx openapi-to-postmanv2@6.4.0 -s contracts/relia-api.openapi.yaml  -o contracts/relia-api.collection.json  -p
npx openapi-to-postmanv2@6.4.0 -s contracts/rail-proxy.openapi.yaml -o contracts/rail-proxy.collection.json -p -O parametersResolution=Example
```

La colección del proxy usa `parametersResolution=Example`: toma los `examples` del YAML en vez de datos falsos y empareja
por nombre los ejemplos de la petición con los de la respuesta. Así cada caso de `POST /payments` (misma moneda, entre
monedas, sin ruta de conversión) queda como un ejemplo con su propio cuerpo.

Haz commit del YAML y de la colección juntos. En Postman, reimporta y elige **Replace** para no duplicar la colección.
