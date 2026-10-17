# Relia API (backend .NET)

Backend de Relia en .NET 10 sobre PostgreSQL. Implementa [contracts/relia-api.openapi.yaml](../contracts/relia-api.openapi.yaml): login, caja de los corresponsales, depósitos, retiros, cargas de cupo, llaves, recibos, liquidaciones y modo demo. No habla con Open Payments: mueve el dinero pidiéndoselo al proxy de riel por [contracts/rail-proxy.openapi.yaml](../contracts/rail-proxy.openapi.yaml). Por qué: [ADR 0001](../docs/adr/0001-proxy-como-contrato-de-riel.md). Spec y tickets: [docs/issues/backend-relia-api.md](../docs/issues/backend-relia-api.md), [docs/issues/backend-relia-api/](../docs/issues/backend-relia-api/) y [docs/wayfinder/relia-api/issues/](../docs/wayfinder/relia-api/issues/). Flujos de punta a punta: [contracts/FLUJOS.md](../contracts/FLUJOS.md).

## Requisitos

- SDK de .NET 10.
- Docker (para PostgreSQL 16).
- El [proxy de riel](../rail-proxy/README.md) corriendo, para los pagos reales. Sin él, las operaciones que tocan el riel responden `503 rail_unavailable`.

## Configurar

Se configura con `appsettings.json`, `appsettings.Development.json`, user-secrets o variables de entorno (`Seccion__Clave`).

```sh
cd backend/API
dotnet user-secrets set "RailProxy:ApiKey" "<la RAIL_PROXY_API_KEY del proxy>"
```

| Variable | Obligatoria | Qué es |
|---|---|---|
| `ConnectionStrings:Relia` | sí | Base de datos. Por defecto el contenedor local: `Host=localhost;Port=5433;Database=relia;Username=relia;Password=relia`. |
| `Jwt:SigningKey` | sí | Firma de los tokens, mínimo 32 bytes. Sin ella el proceso no arranca. Solo trae valor en `appsettings.Development.json`; en Azure va en `Jwt__SigningKey`. |
| `Jwt:Issuer` / `Jwt:Audience` | no | `relia-api` / `relia-frontend`. |
| `Jwt:ExpiresInSeconds` | no | Vida del token. `28800` (8 h). |
| `RailProxy:BaseUrl` | no | URL del proxy. `http://localhost:4000`. |
| `RailProxy:ApiKey` | sí, para pagar | La misma `RAIL_PROXY_API_KEY` del proxy. Se manda en `X-Api-Key`. Vacía por defecto. |
| `RailProxy:TimeoutSeconds` | no | Pasado este plazo el riel cuenta como no disponible (`503`). `10`. |
| `Relia:WalletAddress` | no | Billetera de Relia; la misma `RELIA_WALLET_ADDRESS` del proxy. `$ilp.interledger-test.dev/relia-test1`. |
| `Relia:PublicBaseUrl` | no | Página pública del enlace de invitación. `http://localhost:4200`. |
| `Relia:SweepIntervalSeconds` | no | Cada cuánto el barrido de fondo avanza y vence operaciones y liquidaciones. `15`. |
| `Relia:KeyLinkRateLimitPerMinute` | no | Peticiones por minuto y por IP a `/key-links` antes de `429 rate_limited`. `20`. |
| `DEMO_MODE` | no | Habilita `/demo/*`; sin él responden `404`. `false`, `true` en Development. |
| `RELIA_DB_PORT` | no | Solo para `docker compose`: puerto del host. `5433`. |

## Correr

```sh
cd backend
docker compose up -d --wait          # PostgreSQL 16 en localhost:5433 con db/db_ddl.sql aplicado
cd API
dotnet run --launch-profile http     # http://localhost:5080
```

El perfil `https` escucha además en `https://localhost:7080`. Ambos perfiles usan `ASPNETCORE_ENVIRONMENT=Development`.

Comprobar: abre `http://localhost:5080/swagger` (solo en Development; el documento está en `/openapi/v1.json`). Luego `POST /auth/login` con un usuario de la demo.

Para probar el resto de rutas, usa la colección [contracts/relia-api.collection.json](../contracts/relia-api.collection.json) (ver [contracts/README.md](../contracts/README.md)) o [API/Relia.Api.http](API/Relia.Api.http).

Si el 5433 también está ocupado: `RELIA_DB_PORT=5434 docker compose up -d --wait` y ajusta `ConnectionStrings__Relia`.

## Datos de la demo

Al arrancar contra una base vacía, el backend siembra la demo ([API/Persistence/DemoSeeder.cs](API/Persistence/DemoSeeder.cs)):

| Qué | Datos |
|---|---|
| Admin | `admin@relia.co` / `admin1234` |
| Corresponsales (clave `demo1234`) | `rosa@demo.relia` (Tienda Doña Rosa, en ceros, billetera real `rosa-test`), `luis@demo.relia`, `diana@demo.relia`, `jorge@demo.relia` (con historial de ejemplo y billetera simulada) |
| Llaves | `3000000001` → `$ilp.interledger-test.dev/jeffdev`; `3000000002` sin llave |
| Tarifa | $1.000: 500 Relia y 500 corresponsal |

## Base de datos

- El esquema está en [db/db_ddl.sql](db/db_ddl.sql): tablas, tipos enum y vistas en el esquema `public` de PostgreSQL.
- [docker-compose.yml](docker-compose.yml) levanta `postgres:16` (contenedor `relia-db`, base, usuario y clave `relia`) y monta el DDL en `docker-entrypoint-initdb.d`.
- El DDL solo corre cuando el volumen es nuevo. Si cambias `db/db_ddl.sql`, o quieres empezar de cero: `docker compose down -v && docker compose up -d --wait` y vuelve a arrancar el backend.
- El acceso es con EF Core + Npgsql ([API/Persistence/ReliaDbContext.cs](API/Persistence/ReliaDbContext.cs)). Los enums de C# se mapean a los tipos de Postgres en `ReliaDbContext.MapEnums`: `operation_type`, `load_method`, `operation_status`, `settlement_status`, `payment_rail`, `payment_direction`, `message_channel`, `message_type`.
- No hay migraciones de EF: la fuente de verdad es el DDL.

Después de cualquier flujo, los descuadres deben dar 0:

```sh
docker exec relia-db psql -U relia -d relia -c \
  "select name, cash_mismatch, float_mismatch, commission_mismatch from v_agent_balance_check"
```

## Pruebas

Todavía no hay pruebas automatizadas en el repo. Se verifica con `dotnet build`, la colección de Postman o `Relia.Api.http`, y la consulta de descuadres de arriba.

## Archivos

| Archivo o carpeta | Qué hace |
|---|---|
| [API/Program.cs](API/Program.cs) | Configuración, inyección de dependencias, JWT, rate limit, siembra y Swagger. |
| [API/Configuration/Options.cs](API/Configuration/Options.cs) | Secciones `Jwt`, `RailProxy`, `Relia` y el flag `DEMO_MODE`. |
| [API/Controllers/](API/Controllers/) | Rutas del contrato: auth, agentes, operaciones, llaves, vinculación, liquidaciones y demo. |
| [API/Services/](API/Services/) | Lógica de negocio: operaciones, libro contable (`Ledger.cs`), liquidaciones, llaves, recibos, barrido. |
| [API/Rail/RailClient.cs](API/Rail/RailClient.cs) | Cliente HTTP del proxy de riel (`IRailClient`). |
| [API/Repositories/](API/Repositories/) | Repositorios EF sobre `ReliaDbContext`. |
| [API/Domain/](API/Domain/) | Entidades y enums. |
| [API/Persistence/](API/Persistence/) | `DbContext`, mapeo de enums y siembra de la demo. |
| [API/Idempotency/IdempotencyFilter.cs](API/Idempotency/IdempotencyFilter.cs) | Header `Idempotency-Key`: misma clave y mismo cuerpo devuelven la misma respuesta. |
| [API/Errors/ApiErrors.cs](API/Errors/ApiErrors.cs) | Errores de negocio con su `code` y status. |
| [db/db_ddl.sql](db/db_ddl.sql) | Esquema de PostgreSQL. |

**Depósito** (`POST /agents/{agentId}/deposits`). Relia paga: pide al proxy un pago `outgoing` del monto neto a la billetera de la llave del cliente.

**Retiro** (`POST /agents/{agentId}/withdrawals`). Relia cobra: pide un pago `incoming` del monto cobrado a la billetera del cliente, que debe aprobarlo. Después, el corresponsal entrega el efectivo (`POST /operations/{id}/cash-handover`).

**Carga de cupo** (`POST /agents/{agentId}/quota-top-ups`). Relia cobra al corresponsal. Por billetera va por el proxy (`incoming`); por Bre-B es simulado: un QR que vence a los 10 minutos.

**Liquidación** (`POST /agents/{agentId}/settlements`). Relia paga la comisión al corresponsal (`outgoing`). En modo demo, a las billeteras simuladas se les paga por el riel simulado.

**Barrido de fondo.** Cada `Relia:SweepIntervalSeconds` refresca el pago de las operaciones y liquidaciones pendientes y vence las pasadas de plazo, aunque nadie las consulte.

**Modo demo** (`/demo/*`, con `DEMO_MODE`). Forzar que el próximo pago falle (`faults`, de un solo uso), vencer una operación, reiniciar la demo, simular el pago Bre-B y leer la bandeja de mensajes simulados.

## Errores

Todo error sale en `application/problem+json` con un `code` del enum `ErrorCode` del contrato, también `401 unauthorized`, `403 forbidden` y `429 rate_limited`. Algunos traen datos extra: `insufficient_quota` e `insufficient_cash` traen `shortfall`; `validation_error` trae `errors`. Programa contra el `code`, no contra el texto.
