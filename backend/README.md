# Backend Relia API (.NET 10)

Implementa [contracts/relia-api.openapi.yaml](../contracts/relia-api.openapi.yaml) sobre PostgreSQL y mueve dinero por el
proxy de riel ([contracts/rail-proxy.openapi.yaml](../contracts/rail-proxy.openapi.yaml)). Spec y tickets en
[docs/issues/backend-relia-api.md](../docs/issues/backend-relia-api.md).

## Correr en local

Requisitos: Docker y el SDK de .NET 10.

```bash
cd backend
docker compose up -d --wait          # PostgreSQL 16 en localhost:5433 con db/db_ddl.sql aplicado
cd API
dotnet run --launch-profile http     # http://localhost:5080, Swagger en /swagger
```

Al arrancar contra una base vacía, el backend siembra la demo:

| Qué | Datos |
|---|---|
| Admin | `admin@relia.co` / `admin1234` |
| Corresponsales (clave `demo1234`) | `rosa@demo.relia` (Tienda Doña Rosa, en ceros, billetera real `rosa-test`), `luis@demo.relia`, `diana@demo.relia`, `jorge@demo.relia` (con historial de ejemplo y billetera simulada) |
| Llaves | `3000000001` → `$ilp.interledger-test.dev/jeffdev`; `3000000002` sin llave |
| Tarifa | $1.000: 500 Relia y 500 corresponsal |

Empezar de cero: `docker compose down -v && docker compose up -d --wait` y volver a arrancar el backend.

El DDL solo corre cuando el volumen es nuevo. Si cambias `db/db_ddl.sql`, borra el volumen.

## Configuración

| Clave | Por defecto | Para qué |
|---|---|---|
| `ConnectionStrings:Relia` | contenedor local (puerto 5433) | Base de datos |
| `Jwt:SigningKey` | solo en `appsettings.Development.json` | Firma de los tokens. En Azure: variable `Jwt__SigningKey` (≥ 32 bytes) |
| `RailProxy:BaseUrl` | `http://localhost:4000` | Proxy de riel |
| `RailProxy:ApiKey` | vacío | Misma `RAIL_PROXY_API_KEY` del proxy. Secreto: `dotnet user-secrets set "RailProxy:ApiKey" "..."` |
| `Relia:WalletAddress` | `$ilp.interledger-test.dev/relia-test1` | Billetera de Relia; la misma `RELIA_WALLET_ADDRESS` del proxy |
| `Relia:PublicBaseUrl` | `http://localhost:4200` | Página pública del enlace de invitación |
| `DEMO_MODE` | `true` en Development | Habilita `/demo/*` |

Si 5433 también está ocupado: `RELIA_DB_PORT=5434 docker compose up -d` y ajusta `ConnectionStrings__Relia`.

## Verificar

Sin pruebas automatizadas: se verifica con [contracts/relia-api.collection.json](../contracts/relia-api.collection.json)
(Postman) o con [API/Relia.Api.http](API/Relia.Api.http). Después de cualquier flujo, los descuadres deben dar 0:

```bash
docker exec relia-db psql -U relia -d relia -c \
  "select name, cash_mismatch, float_mismatch, commission_mismatch from v_agent_balance_check"
```
