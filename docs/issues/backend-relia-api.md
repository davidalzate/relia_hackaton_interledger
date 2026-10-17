# Backend .NET de Relia: implementar `relia-api.openapi.yaml` sobre PostgreSQL en Docker y el proxy de riel

Type: spec
Status: open
Blocked by:
Labels: ready-for-agent
Supersedes: [01 Implementar backend Relia API](../wayfinder/relia-api/issues/01-implementar-backend-relia-api.md) (asumía el esquema v0.2 en español y un proyecto de pruebas)

## Problem Statement

El frontend de Relia ya tiene las pantallas del corresponsal, del admin, de la página pública de vinculación y del Modo demo. El contrato [relia-api.openapi.yaml](../../contracts/relia-api.openapi.yaml) y su colección de Postman ya están cerrados. El proxy de riel ya implementa [rail-proxy.openapi.yaml](../../contracts/rail-proxy.openapi.yaml) sobre la red de prueba de Interledger.

El backend .NET, en cambio, solo tiene el esqueleto: controladores, DTOs, repositorios e interfaces de servicio. Todos los servicios responden `501 not_implemented`. Además, su mapeo de EF apunta al esquema v0.2 en español (`corresponsales`, `cupos`, `operaciones`…), mientras que la base real es la de [db_ddl.sql](../../backend/db/db_ddl.sql), en inglés (`agents`, `agent_balances`, `operations`…). No hay una base que se pueda levantar en local. Ese DDL es un volcado de Azure y no corre tal cual en un Postgres limpio (`AUTHORIZATION azure_pg_admin`, triggers declarados antes que sus funciones).

Mientras tanto nadie puede iniciar sesión, mover dinero ni correr el guion de demo ([FLUJOS.md §12](../../contracts/FLUJOS.md#12-guion-de-demo-completo)). Front y proxy no tienen contra qué integrarse.

## Solution

Un backend .NET 10 que cumple `relia-api.openapi.yaml` endpoint por endpoint. Se construye sobre el proyecto que ya existe y reemplaza cada stub por su implementación.

- **Base de datos local**: `docker compose up` levanta PostgreSQL 16 y aplica `db_ddl.sql` al crear el volumen. El backend arranca contra esa base y, si está vacía, siembra los datos de la demo.
- **Riel**: el backend mueve dinero solo a través del proxy de riel (cobrar a una billetera, pagar a una billetera, consultar, cancelar y resolver pointers) y no conoce Open Payments ([ADR 0001](../adr/0001-proxy-como-contrato-de-riel.md)).
- **Qué puede hacer cada actor**:
  - el corresponsal inicia sesión, ve sus saldos, deposita, retira, carga cupo, ajusta su efectivo y consulta su historial y sus recibos;
  - el usuario vincula su celular a su billetera con OTP;
  - el admin ve la red y paga comisiones;
  - quien presenta maneja el guion desde el Modo demo.
- **Verificación**: manual, con la colección de Postman de la Relia API contra el backend, la base en Docker y el proxy real.

## User Stories

### Arranque local

1. Como desarrollador, quiero levantar PostgreSQL con `docker compose up`, para tener la base del backend sin instalar nada más.
2. Como desarrollador, quiero que el contenedor aplique `db_ddl.sql` la primera vez que crea el volumen, para tener el esquema completo (tablas, enums, triggers, vistas) sin pasos manuales.
3. Como desarrollador, quiero que el mismo `db_ddl.sql` siga corriendo en Azure, para no mantener dos DDL.
4. Como desarrollador, quiero borrar el volumen y volver a levantar para empezar de cero.
5. Como desarrollador, quiero que el backend, al arrancar contra una base vacía, siembre la tarifa vigente, un admin y los corresponsales de la demo, para poder iniciar sesión de inmediato.
6. Como desarrollador, quiero que la cadena de conexión por defecto apunte al contenedor local, para correr el backend sin tocar configuración.
7. Como desarrollador, quiero configurar la URL del proxy, su `X-Api-Key`, el payment pointer de Relia, el secreto JWT, `DEMO_MODE` y la URL base de la página pública, para conectar el backend con lo demás.
8. Como desarrollador, quiero que el backend escuche en `http://localhost:5080`, el `baseUrl` de la colección de Postman.

### Sesión

9. Como corresponsal, quiero iniciar sesión con correo y contraseña, para entrar a mi Caja.
10. Como admin, quiero iniciar sesión con correo y contraseña, para entrar al panel admin.
11. Como frontend, quiero recibir `accessToken`, `expiresIn`, `role` y `agentId` (solo `agent`), para saber a qué pantalla ir.
12. Como corresponsal, quiero que credenciales malas o una cuenta inactiva respondan `401 invalid_credentials`.
13. Como frontend, quiero que un cuerpo inválido responda `422 validation_error` con `errors` por campo.
14. Como frontend, quiero que `GET /me` devuelva el perfil, y el corresponsal con saldos si el rol es `agent`, para restaurar la sesión al recargar.
15. Como frontend, quiero que cualquier ruta protegida sin token válido responda `401 unauthorized` en `problem+json`.
16. Como Relia, quiero las contraseñas guardadas con hash, nunca en claro.

### Caja y saldos

17. Como corresponsal, quiero ver cupo, cupo reservado, efectivo en caja, efectivo reservado, comisión acumulada y comisión liquidada.
18. Como corresponsal, quiero que las reservas salgan de las operaciones pendientes: los depósitos reservan el monto cobrado de cupo y los retiros reservan el monto neto de efectivo.
19. Como corresponsal, quiero ver mis últimas 3 entradas en la Caja con `GET /agents/{agentId}/history?limit=3`.
20. Como corresponsal, quiero `403 forbidden` al leer otro corresponsal y `404 not_found` si el corresponsal no existe.
21. Como admin, quiero leer cualquier corresponsal y su historial.

### Historial

22. Como corresponsal, quiero mi historial unificado (operaciones en cualquier estado, ajustes de efectivo y liquidaciones), el más reciente primero.
23. Como corresponsal, quiero paginarlo con `cursor`/`nextCursor` y `limit` (1–100, por defecto 20).
24. Como corresponsal, quiero filtrarlo por `kind` y `status`.
25. Como corresponsal, quiero que las entradas no completadas muestren antes = después con el último saldo conocido.
26. Como corresponsal, quiero ver las liquidaciones pagadas como `completed` y las anuladas como `cancelled`.
27. Como corresponsal, quiero ver el motivo de un ajuste o de un fallo y el `failureCode` de la operación.

### Efectivo en caja

28. Como corresponsal, quiero sumar efectivo (`mode: add`, monto > 0) o fijar el total (`mode: set`).
29. Como corresponsal, quiero ver el efectivo antes y después en la respuesta `201`.
30. Como corresponsal, quiero `422 insufficient_cash` con `shortfall` si fijo un total por debajo del efectivo reservado.
31. Como corresponsal, quiero `422 no_change` si el ajuste no cambia nada.
32. Como corresponsal, quiero el motivo por defecto "Asignación de efectivo" o "Ajuste de efectivo".
33. Como corresponsal, quiero que repetir el ajuste con la misma `Idempotency-Key` no sume dos veces.
34. Como Relia, quiero que el ajuste no toque cupo ni comisión.

### Desglose en vivo

35. Como corresponsal, quiero un desglose de depósito o retiro mientras escribo el monto, con tarifa, neto y cobrado.
36. Como corresponsal, quiero que con "valor redondo" el usuario reciba el monto exacto y pague la tarifa aparte, y sin él la tarifa se descuente.
37. Como corresponsal, quiero ver cupo, efectivo y comisión después de la operación, contando las reservas.
38. Como corresponsal, quiero que el desglose responda siempre `200` y traiga en `violations` `amount_too_low`, `insufficient_quota` o `insufficient_cash` con `shortfall`.
39. Como Relia, quiero que la tarifa salga de la tarifa vigente de la base ($1.000: 500 Relia y 500 corresponsal) y se copie en la operación.

### Llaves e invitación

40. Como corresponsal, quiero validar el celular del usuario y ver su billetera enmascarada.
41. Como corresponsal, quiero `404 key_not_found` si el celular no tiene llave, para ofrecer la invitación.
42. Como corresponsal, quiero invitar por WhatsApp a un celular sin llave y recibir `waLink`, `message` y `sentAt`.
43. Como quien presenta, quiero ver la invitación en el teléfono simulado de ese celular, con el enlace a la página pública y el celular precargado.
44. Como corresponsal, quiero `409 key_already_linked` si invito un celular que ya tiene llave.

### Vinculación pública

45. Como usuario, quiero escribir mi celular y mi payment pointer y recibir un código por SMS simulado, sin iniciar sesión.
46. Como usuario, quiero `409 key_already_linked` si mi celular ya tiene llave.
47. Como Relia, quiero `409 phone_reserved` si el celular o la billetera son de un corresponsal o de Relia.
48. Como usuario, quiero `409 wallet_already_linked`, sin revelar a qué celular, si la billetera ya está vinculada.
49. Como usuario, quiero que el pointer se resuelva en el riel antes de enviar el código, para recibir `422 wallet_not_found` si no existe y `503 rail_unavailable` si el riel no responde.
50. Como usuario, quiero un OTP de 6 dígitos que venza en 5 minutos, con 5 intentos, guardado como hash.
51. Como usuario, quiero que "Reenviar código" invalide el desafío anterior.
52. Como usuario, quiero `422 invalid_otp` con `attemptsRemaining`, y `otp_expired` u `otp_attempts_exceeded` cuando corresponda.
53. Como usuario, quiero que el código correcto cree la llave y me devuelva el pointer completo y `linkedAt`.
54. Como Relia, quiero que verificar responda el mismo `409` que al iniciar si otro flujo vinculó el celular o la billetera mientras tanto.
55. Como Relia, quiero limitar los intentos con `429 rate_limited` y `Retry-After`.
56. Como Relia, quiero que una llave no se pueda cambiar ni borrar (el trigger de la base lo impide).

### Depósito

57. Como corresponsal, quiero crear un depósito al confirmar que recibí el efectivo, para que Relia pague el neto a la billetera del usuario.
58. Como corresponsal, quiero recibir `202` con la operación `pending` y `Location`, y consultar su estado cada 1–2 s.
59. Como corresponsal, quiero que el depósito pendiente reserve el monto cobrado de mi cupo.
60. Como corresponsal, quiero `422 insufficient_quota` con `shortfall` si no me alcanza el cupo.
61. Como corresponsal, quiero `404 key_not_found` si el celular no tiene llave.
62. Como corresponsal, quiero `503 rail_unavailable` sin crear nada si el proxy no acepta el pago (riel caído o sin permiso permanente), para reintentar con la misma clave.
63. Como corresponsal, quiero que al completarse baje el cupo, suban el efectivo y la comisión, se congele el recibo y quede el movimiento, todo en una transacción.
64. Como corresponsal, quiero que un pago de riel `failed` deje la operación `failed`/`payment_failed` y libere la reserva.
65. Como usuario, quiero un SMS simulado de aviso de depósito al completarse.

### Retiro

66. Como corresponsal, quiero crear un retiro que reserve el neto de mi efectivo y le cobre el monto cobrado a la billetera del usuario.
67. Como corresponsal, quiero recibir `approvalUrl` y `expiresAt` (5 min).
68. Como usuario, quiero recibir la `approvalUrl` por SMS simulado de solicitud de pago.
69. Como corresponsal, quiero `422 insufficient_cash` con `shortfall` si no me alcanza el efectivo.
70. Como corresponsal, quiero que el retiro pase a `completed` cuando el usuario aprueba y el pago llega, y entonces suban el cupo y la comisión y baje el efectivo.
71. Como corresponsal, quiero ver `failed`/`customer_rejected`, `expired`/`request_expired`, `cancelled`/`request_cancelled` o `failed`/`payment_failed` según el pago de riel, y que cada uno libere la reserva.
72. Como corresponsal, quiero cancelar un retiro pendiente antes de la aprobación, y recibir `409 operation_not_cancellable` después.
73. Como corresponsal, quiero confirmar la entrega del efectivo de un retiro `completed`, sin tocar saldos y de forma idempotente, y ver `cashHandedOverAt`.
74. Como corresponsal, quiero `409 operation_not_completed` si confirmo la entrega de un retiro no completado.
75. Como Relia, quiero vencer el retiro pasado su `expiresAt` aunque el proxy no lo haya reportado, y cancelarlo en el riel.

### Carga de cupo

76. Como corresponsal, quiero cargar cupo por Bre-B y recibir un `qrPayload` simulado que vence en 10 min, sin tocar el riel.
77. Como corresponsal, quiero cargar cupo por Wallet: Relia le cobra a mi billetera registrada, con `approvalUrl` y 5 min de plazo.
78. Como corresponsal, quiero que la carga completada suba mi cupo sin tarifa (`fee` en 0, neto = cobrado = monto).
79. Como corresponsal, quiero cancelar o ver vencer una carga pendiente igual que un retiro.
80. Como corresponsal, quiero ver la carga en mi historial como `quotaTopUp`.

### Seguimiento de pagos de riel

81. Como corresponsal, quiero que `GET /operations/{operationId}` refleje el último estado del pago, y que el backend consulte el proxy como máximo una vez por segundo por pago.
82. Como Relia, quiero un barrido de fondo cada 15 s que avance operaciones y liquidaciones pendientes aunque nadie las consulte.
83. Como Relia, quiero descartar lecturas del proxy con una `version` menor a la ya procesada.
84. Como Relia, quiero que la operación siga `pending` si el proxy deja de responder, y que el siguiente ciclo reintente.
85. Como Relia, quiero que cada cambio de estado de una operación quede en `operation_events` y cada llamada y respuesta del riel en `payment_events`.
86. Como Relia, quiero guardar en `payments` el id del pago en el proxy, las URLs del riel, `debitAmount`, `receivedAmount` y el error del riel, para conciliar y trazar por billetera.

### Recibos

87. Como corresponsal, quiero ver el recibo congelado de un depósito o retiro `completed`, con `code`, `contentHash` y datos enmascarados.
88. Como corresponsal, quiero que `isCopy` sea true desde la segunda emisión.
89. Como Relia, quiero registrar cada emisión por canal (`screen`, `print`, `pdf`, `sms`, `whatsapp`).
90. Como corresponsal, quiero `409 receipt_not_available` en cargas y `409 operation_not_completed` en operaciones no completadas.
91. Como corresponsal, quiero enviar el recibo por SMS o WhatsApp al usuario y verlo en el teléfono simulado.
92. Como usuario, quiero que mi celular y mi billetera salgan enmascarados en toda respuesta al corresponsal y al admin.

### Admin

93. Como admin, quiero ver en una sola llamada todos los corresponsales con métricas y los totales de la red.
94. Como admin, quiero que los montos movidos cuenten solo depósitos y retiros completados, con el monto solicitado (M).
95. Como admin, quiero ver la comisión pagada hoy (America/Bogota).
96. Como admin, quiero listar liquidaciones filtradas por `from`, `to`, `agentId` y `status`, paginadas.
97. Como admin, quiero pagar toda la comisión acumulada de un corresponsal a su billetera, con el monto fijo desde que se crea.
98. Como admin, quiero que el período vaya desde el día siguiente a la última liquidación pagada (o desde la primera operación) hasta hoy.
99. Como admin, quiero `409 settlement_in_progress` si ya hay una pendiente, `422 no_commission` si la comisión es $0 y `503 rail_unavailable` sin crear nada si el riel no acepta el pago.
100. Como admin, quiero que la liquidación pase a `paid`, o a `voided` con `voidReason`, según el pago de riel.
101. Como corresponsal, quiero un SMS simulado de aviso cuando me pagan la comisión.
102. Como admin, quiero que un corresponsal con billetera simulada (`walletSimulated`) se liquide sin pasar por el riel.
103. Como Relia, quiero que el admin reciba `403 forbidden` al crear depósitos, retiros, cargas o ajustes, al cancelar y al confirmar entregas.
104. Como corresponsal, quiero ver solo mis liquidaciones, con el filtro `agentId` ignorado, y `403` al leer una ajena.

### Modo demo

105. Como quien presenta, quiero que `/demo/*` funcione sin login con `DEMO_MODE=true` y responda `404` sin el flag.
106. Como quien presenta, quiero simular que llegó el pago Bre-B de una carga pendiente, y recibir `409` si no es una carga Bre-B pendiente.
107. Como quien presenta, quiero ver la bandeja del teléfono simulado, global o por celular, paginada y con `unreadCount`.
108. Como quien presenta, quiero marcar los mensajes como leídos al abrir el teléfono, de un celular o de todos.
109. Como quien presenta, quiero ver las solicitudes de pago pendientes de un celular (usuario o corresponsal) con su `approvalUrl`.
110. Como quien presenta, quiero que "El próximo pago falla" deje la siguiente operación o liquidación en `failed`/`payment_failed` (o `voided`) sin llamar al riel, y se apague sola.
111. Como quien presenta, quiero vencer ya un retiro o una carga pendiente, cancelando en el riel, y recibir `409` si no está pendiente o `503` si el riel no responde.
112. Como quien presenta, quiero reiniciar la demo: cancelar en el riel los cobros pendientes, vaciar los datos, volver a sembrar y apagar los fallos forzados.

### Transversal

113. Como frontend, quiero todos los errores en `application/problem+json` con un `code` del enum del contrato.
114. Como frontend, quiero que toda creación con `Idempotency-Key` devuelva el recurso existente si repito clave y cuerpo, y `409 idempotency_conflict` si cambio el cuerpo.
115. Como frontend, quiero que un 4xx o un 503 al crear no deje nada creado, para reintentar con la misma clave.
116. Como frontend, quiero las fechas en RFC 3339 UTC, el dinero en enteros COP y los enums en camelCase como dice el contrato.
117. Como Relia, quiero que la vista de chequeo de saldos (`v_agent_balance_check`) dé descuadres en 0 después de cualquier secuencia de operaciones.

## Implementation Decisions

### Alcance y fuentes de verdad

- Solo el backend .NET y su base local. El proxy de riel ya existe y se consume solo por HTTP según `rail-proxy.openapi.yaml`.
- `relia-api.openapi.yaml` manda: rutas, cuerpos, códigos de estado y `code` de error salen tal cual. Si algo no se puede implementar, se cambia primero el contrato.
- Se construye sobre el proyecto existente: se conservan los controladores, los DTOs, `ApiException` con su handler, el handler de 401/403 en `problem+json`, `ICurrentUser`, el patrón servicio + repositorio + `IUnitOfWork` y `TimeProvider`.
- Faltan controladores para `GET /agents`, `POST /agents/{agentId}/settlements`, `/key-links` y `/demo/*`.
- `FeatureInDevelopmentException` (501) desaparece cuando no quede ningún stub.

### Base de datos

- **`db_ddl.sql` es el único DDL** y debe correr igual en un Postgres 16 limpio y en Azure. Cambios al volcado:
  - quitar `CREATE SCHEMA public AUTHORIZATION azure_pg_admin` (el esquema `public` ya existe en ambos);
  - declarar las funciones antes de las tablas y triggers que las usan;
  - quitar las secuencias sueltas que nadie usa, porque las tablas usan identity;
  - agregar lo que falta para cumplir el contrato:
    - tabla `idempotency_records`: alcance (ruta + actor), clave, hash del cuerpo, estado HTTP, cuerpo de la respuesta, id del recurso y fecha, con unicidad por (alcance, clave);
    - `operations.failure_code` y `operations.cash_handed_over_at`, que `protect_operation` no congela;
    - `agents.wallet_simulated` (bool, por defecto false);
    - `payments.rail_payment_id` (uuid único, id del pago en el proxy) y `payments.rail_version` (último `version` procesado).
- **Contenedor**: un compose en el backend con `postgres:16`, base, usuario y clave `relia`, puerto 5432 y volumen con nombre. Monta `db_ddl.sql` como script de inicio, que solo corre cuando el volumen es nuevo. Rehacer la base es borrar el volumen.
- **Siembra en el backend, una sola fuente**: una rutina de siembra crea la tarifa vigente (1.000 / 500 / 500), un admin, los 4 corresponsales de la demo con sus historiales de ejemplo (Rosa en ceros) y la llave `3000000001 → $ilp.interledger-test.dev/jeffdev`. El `3000000002` queda sin llave.
  - Se ejecuta al arrancar si no hay admins, y en `POST /demo/reset`.
  - Las contraseñas se guardan con el `PasswordHasher` de ASP.NET.
  - Los historiales de ejemplo se insertan como operaciones completadas, con pagos completados marcados como siembra en `rail_details`, para que pasen las validaciones de la base.
- **Mapeo EF al esquema en inglés**: se reescribe el mapeo de `ReliaDbContext` (tablas, columnas y enums de PostgreSQL) a los nombres de `db_ddl.sql`. Sin migraciones: el esquema lo crea el DDL. Las traducciones de enum viven en la capa de persistencia o en el mapeo a DTO:

  | Base | API |
  |---|---|
  | `float_load` | `quotaTopUp` |
  | `bre_b` | `breB` |
  | `open_payments` / `bre_b_simulated` | `openPayments` / `breBSimulated` |
  | `wallet_invitation`, `deposit_notice`, `payment_request`, `receipt`, `commission_notice` | `invitacion_wallet`, `aviso_deposito`, `solicitud_pago`, `comprobante`, `aviso_comision` |

- **Lo que hace la base y lo que hace la aplicación**:
  - La base ya hace cumplir:
    - inmutabilidad de llaves, recibos, eventos y movimientos;
    - montos fijos de la operación y estados finales sin regreso;
    - un `agent_balances` por corresponsal (trigger al insertar);
    - recibo congelado al completar (trigger `issue_receipt_on_completion`), con código aleatorio y SHA-256;
    - `isCopy` por emisión;
    - validación de cada movimiento contra su operación o liquidación y su pago completado;
    - una sola liquidación pendiente por corresponsal.
  - La aplicación hace, en una transacción con `SELECT … FOR UPDATE` sobre `agent_balances`:
    - leer saldos y reservas;
    - actualizar cupo, comisión, efectivo y comisión liquidada;
    - escribir el movimiento en `ledger_entries`.
  - **Orden al completar**, que exigen los triggers: (1) pago `completed`; (2) operación `completed` (congela el recibo); (3) movimiento en `ledger_entries` y actualización de `agent_balances`; (4) evento y mensaje simulado.
- **Variación de saldos por tipo**, según `ck_ledger_signs` y `validate_ledger_entry_payment`:

  | Tipo | Cupo | Efectivo | Comisión |
  |---|---|---|---|
  | Depósito | −cobrado | +cobrado | +comisión del corresponsal |
  | Retiro | +neto | −neto | +comisión del corresponsal |
  | Carga | +monto | 0 | 0 |
  | Liquidación | 0 | 0 | −monto (y `settled_commission_total` sube el monto) |
  | Ajuste | 0 | ± variación | 0 |

- **Todo movimiento de operación o liquidación tiene un pago**, porque la base lo exige:
  - la carga Bre-B y la liquidación con `walletSimulated` crean un registro en `payments` con riel `bre_b_simulated`, marcado como simulado en `rail_details`, que se completa sin llamar al proxy;
  - el `source`/`destination` de cada pago usa el payment pointer de Relia de la configuración.
- **Historial**: se arma desde la vista `v_agent_history` (operaciones, ajustes y liquidaciones con antes y después), con paginación por cursor sobre (`occurred_at`, id) y filtros `kind`/`status`.
- **Panel admin**: se arma desde la vista `v_admin_agent_summary`.
- **Recibos**: el `data` congelado por la base (nombres en snake_case, celular y pointer completos) se traduce a `ReceiptData` del contrato y se enmascara al responder. `code`, `contentHash` e `issuedAt` salen del registro en `receipts`.

### Módulos

- **Auth**: login contra `agents` y `admins` (activos, contraseña verificada con hash). Emite un JWT con `sub`, `role` y `agentId` y vida configurable. La regla "agente solo lo suyo, admin lee todo pero no opera" se aplica en un solo lugar que reutilizan los servicios.
- **Ledger de saldos**: único lugar que cambia `agent_balances` y escribe `ledger_entries`. Calcula las reservas a partir de las operaciones pendientes y aplica la transición a `completed` con el orden de arriba.
- **Tarifas y desglose**: una función pura de (tipo, monto, valor redondo, tarifa) → `fee`, `netAmount`, `chargedAmount`. La usan el desglose, los depósitos y los retiros.
- **Operaciones**:
  - crea depósito, retiro y carga junto con su pago y llama al riel;
  - lleva la máquina de estados de la operación y la traducción del estado del pago de riel:

    | Pago de riel | Operación |
    |---|---|
    | `pending_approval`, `processing` | `pending` |
    | `completed` | `completed` |
    | `rejected` | `failed`/`customer_rejected` |
    | `expired` | `expired`/`request_expired` |
    | `cancelled` | `cancelled`/`request_cancelled` |
    | `failed` | `failed`/`payment_failed` |

  - guarda `failure_code` en la operación y el `failureDetail` sale del error del pago.
- **Cliente de riel** (`IRailClient`), la única costura hacia afuera:
  - cliente HTTP tipado con resolver pointer, crear pago, consultar pago y cancelar pago;
  - manda `X-Api-Key` y, como `Idempotency-Key`, el `payments.id` del backend;
  - convierte COP enteros a `{ value: pesos × 100, assetCode: "COP", assetScale: 2 }` y de vuelta;
  - trata un `200` por idempotencia igual que un `201`;
  - traduce los errores del proxy:

    | Proxy | Backend |
    |---|---|
    | `rail_unavailable`, `relia_grant_unavailable`, timeout o error de red | no disponible → `503 rail_unavailable` |
    | `wallet_not_found` | billetera inexistente |
    | `payment_not_cancellable` | no cancelable → `409 operation_not_cancellable` |
    | `unsupported_asset`, `same_wallet`, `idempotency_conflict` | error de dominio explícito |

- **Sincronizador**:
  - refresca el pago al leer la operación o la liquidación, como máximo una vez por segundo por pago;
  - un servicio de fondo hace un barrido cada 15 s;
  - descarta lecturas con `version` ≤ `rail_version`;
  - vence en el backend las operaciones pasadas de `expiresAt` (5 min Wallet, 10 min Bre-B) y cancela en el riel. Si el riel no responde, la operación queda `pending` y se reintenta en el siguiente ciclo.
- **Llaves y vinculación**:
  - búsqueda de llave e invitación (enlace `wa.me` con la URL pública y `?phone=`);
  - desafíos OTP en `otp_challenges`: código hasheado, 5 min, 5 intentos, y un reenvío invalida el desafío anterior;
  - el orden de validación del contrato;
  - celulares y billeteras reservados: los de los corresponsales y la de Relia;
  - rate limit en memoria con `429` y `Retry-After`.
- **Recibos**: lectura del recibo congelado, emisiones en `receipt_issuances` (con `requested_by` = `agent`) y envío por la bandeja simulada.
- **Liquidaciones**:
  - monto y período fijos al crear y `destination_payment_pointer` copiado del corresponsal;
  - `pending → paid | voided`; se sigue en la lectura y en el barrido;
  - el atajo `walletSimulated`;
  - un SMS simulado al pasar a `paid`.
- **Mensajería simulada**: escribe en `simulated_messages` para todos los tipos y alimenta la bandeja y `unreadCount`.
- **Idempotencia**:
  - un filtro o servicio común para toda creación con `Idempotency-Key`, sobre `idempotency_records`, con el alcance (ruta + actor) y el hash del cuerpo;
  - misma clave y mismo cuerpo → misma respuesta; otro cuerpo → `409 idempotency_conflict`;
  - con 4xx o 503 no se guarda nada;
  - `ux_operations_idempotency` queda como respaldo en la base.
- **Modo demo**:
  - las rutas `/demo/*` responden 404 sin `DEMO_MODE=true`;
  - `failNextPayment` vive en memoria y es de un solo uso;
  - "vencer ya";
  - `reset`: cancela en el riel los cobros pendientes, hace `TRUNCATE … RESTART IDENTITY CASCADE` de los datos (los triggers de inmutabilidad son por fila y no bloquean `TRUNCATE`), vuelve a sembrar y apaga los fallos.
- **Errores**: un único mapeo de errores de dominio a `problem+json`, con `code`, `shortfall`, `attemptsRemaining` y `errors` por campo (ya existe la base).

### Transversal

- **Reloj**: `TimeProvider` para todos los plazos y para "hoy" en America/Bogota.
- **Enmascarado**: la base guarda datos completos y el backend enmascara al responder (`300 *** 0001`, `$ilp...test.dev/us***`). La única excepción es la respuesta de `verify`, que devuelve el pointer completo.
- **Configuración**: la conexión por defecto apunta al contenedor (`Host=localhost;Database=relia;Username=relia;Password=relia`). Además se configuran la URL base del proxy (`http://localhost:4000`), su API key, el payment pointer de Relia (el mismo `RELIA_WALLET_ADDRESS` del proxy), el JWT, `DEMO_MODE` y la URL base de la página pública. Los secretos van en user-secrets o en variables de entorno, no en el repo.

## Testing Decisions

- **Sin proyecto de pruebas automatizadas** (decisión del equipo para el hackathon, igual que en el proxy). No se crean pruebas unitarias ni de integración.
- **Una sola costura, la más alta**: la API HTTP, verificada a mano con [relia-api.collection.json](../../contracts/relia-api.collection.json) contra el backend corriendo, la base en Docker y el proxy de riel real en la red de prueba. Una buena verificación mira solo comportamiento externo:
  - estado HTTP, `content-type`, cuerpo y `code`, comparados con el contrato;
  - efectos visibles desde otros endpoints: saldos en `GET /agents/{id}`, historial, bandeja de `/demo/messages` y solicitudes en `/demo/payment-requests`.
- **Chequeo de consistencia**: después de cada flujo, `v_agent_balance_check` debe dar `cash_mismatch`, `float_mismatch` y `commission_mismatch` en 0. Es la única consulta SQL de la verificación.
- **Qué se verifica como mínimo**:
  - cada carpeta de la colección;
  - los 8 pasos del guion de FLUJOS.md §12, de `POST /demo/reset` al pago de comisión;
  - cada fila de la tabla de traducción de estados (rechazar y dejar vencer en la wallet de prueba, cancelar, y `failNextPayment` para `payment_failed`);
  - la idempotencia (repetición, conflicto, reintento después de un error);
  - los `403` por rol;
  - el arranque en limpio (borrar el volumen, `docker compose up`, arrancar el backend, iniciar sesión).
- **Errores difíciles de provocar con el proxy real**: el `503` se prueba apagando el proxy, o con el permiso de Relia sin aprobar para `relia_grant_unavailable`.
- **Prior art**: el proxy de riel se verificó igual ([spec del proxy](proxy-de-riel-node.md), ticket 09 de ensayo contra la red de prueba). El resultado de cada ensayo se anota en su ticket.

## Out of Scope

- El proxy de riel y todo lo de Open Payments (grants, quotes, el permiso permanente de Relia).
- Cambios al frontend.
- Pruebas automatizadas y CI.
- Despliegue en Azure. El DDL debe seguir corriendo allí, pero no se despliega en este trabajo.
- Bre-B real, SMS y WhatsApp reales, y verificar que el usuario sea dueño de la billetera.
- Conciliación de cupo contra el riel y saldo de la billetera de Relia en el panel admin.
- Reemplazar la billetera de una llave y consultar recibos con código + OTP (el propósito `receipt_lookup` existe en la base pero no en el contrato).
- Rediseñar el esquema más allá de los agregados listados arriba.

## Further Notes

- **Reemplaza la spec anterior**. [01 Implementar backend Relia API](../wayfinder/relia-api/issues/01-implementar-backend-relia-api.md) y sus tickets 02–17 asumen el esquema v0.2 en español (`docs/relia_schema_hackathon_v0.2.sql`) y Testcontainers. Conviene marcarlos como reemplazados y cortar tickets nuevos a partir de esta spec. El orden útil es:
  1. base en Docker, mapeo en inglés, siembra, login y `/me`;
  2. cliente de riel;
  3. saldos e historial;
  4. ajustes e idempotencia;
  5. desglose;
  6. llaves, bandeja y vinculación;
  7. depósito;
  8. recibos;
  9. retiro;
  10. carga;
  11. barrido;
  12. admin;
  13. liquidaciones;
  14. demo;
  15. ensayo del guion.
- **Precondición de la demo**: el operador aprueba el permiso permanente de Relia en el proxy ([FLUJOS.md §0](../../contracts/FLUJOS.md#0-preparación-del-riel-operador-una-vez)). Sin él, depósitos y liquidaciones responden `503 rail_unavailable`.
- **Tickets abiertos que afectan la demo, no la implementación**:
  - [11 billetera para el celular sin llave](../wayfinder/contratos-api/issues/11-billetera-para-el-celular-sin-llave.md): el paso 6 del guion necesita una billetera COP libre;
  - [12 moneda al vincular llave](../wayfinder/contratos-api/issues/12-moneda-al-vincular-llave.md): hoy el contrato no rechaza billeteras que no sean COP.
- **Ojo con la regla de depósito de la base**: el cupo baja el monto *cobrado* (neto + tarifa), no el neto. Así lo valida `validate_ledger_entry_payment`, y el desglose y las reservas deben usar la misma regla.
- Vocabulario según [GLOSSARY.md](../../GLOSSARY.md): corresponsal (`agent`), usuario (`customer`), llave (`key`), cupo (`quota`), efectivo en caja (`cashOnHand`), liquidación (`settlement`), movimiento (`movement`).
