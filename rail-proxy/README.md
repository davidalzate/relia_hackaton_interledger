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

## Errores

Todo error sale en `application/problem+json` con un `code` del enum `ErrorCode` del contrato. Programa contra el `code`, no contra el texto.
