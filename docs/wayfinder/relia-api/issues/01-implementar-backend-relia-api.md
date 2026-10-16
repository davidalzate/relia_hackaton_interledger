# Implementar el backend .NET según el contrato de la Relia API

Type: spec
Status: open
Blocked by:
Labels: ready-for-agent

## Problem Statement

El frontend de Relia (Angular) ya tiene pantallas y servicios para el corresponsal, el admin, la página pública de vinculación y el Modo demo, y los contratos `contracts/relia-api.openapi.yaml` y `contracts/rail-proxy.openapi.yaml` ya pasaron el lint. Pero el backend .NET todavía es la plantilla vacía de ASP.NET: no hay login, saldos, operaciones, llaves, liquidaciones ni Modo demo. Sin backend no se puede correr el guion de demo ([prototype.md §12](../../../prototype.md), [FLUJOS.md §12](../../../../contracts/FLUJOS.md#12-guion-de-demo-completo)) y front y proxy no tienen contra qué integrarse.

## Solution

Un backend .NET 10 que cumple `relia-api.openapi.yaml` endpoint por endpoint. Guarda todo en PostgreSQL con el esquema [relia_schema_hackathon_v0.2.sql](../../../relia_schema_hackathon_v0.2.sql) y habla con el Rail Proxy como cliente de `rail-proxy.openapi.yaml`, sin conocer Open Payments ([ADR 0001](../../../adr/0001-proxy-como-contrato-de-riel.md)). El corresponsal entra, ve sus saldos, deposita, retira, carga cupo, ajusta su efectivo y consulta su historial y sus recibos. El usuario vincula su celular a su billetera con OTP. El admin ve la red y paga comisiones. Quien presenta maneja todo el guion desde el Modo demo. El proxy Node queda fuera de esta spec. Lo construye otra persona contra el mismo contrato.

## User Stories

### Sesión

1. Como corresponsal, quiero iniciar sesión con correo y contraseña, para entrar a mi Caja.
2. Como admin, quiero iniciar sesión con correo y contraseña, para entrar al panel admin.
3. Como usuario del frontend, quiero recibir el rol y el `agentId` al iniciar sesión, para saber a qué pantalla ir.
4. Como corresponsal, quiero que credenciales malas respondan `401 invalid_credentials`, para ver "Correo o contraseña incorrectos.".
5. Como frontend, quiero que `GET /me` devuelva el perfil, y el corresponsal con saldos si el rol es `agent`, para restaurar la sesión al recargar.
6. Como frontend, quiero que cualquier ruta protegida sin token válido responda `401 unauthorized`.

### Caja y saldos

7. Como corresponsal, quiero ver cupo, cupo reservado, efectivo en caja, efectivo reservado, comisión acumulada y comisión liquidada, para saber cuánto puedo operar.
8. Como corresponsal, quiero que las reservas se calculen con las operaciones pendientes, para no prometer cupo o efectivo ya comprometidos.
9. Como corresponsal, quiero ver mis últimas 3 operaciones en la Caja con `GET /agents/{agentId}/history?limit=3`.
10. Como corresponsal, quiero que leer otro corresponsal me responda `403 forbidden`, para que mis datos y los ajenos queden separados.
11. Como admin, quiero leer cualquier corresponsal, para ver su detalle.

### Efectivo en caja

12. Como corresponsal, quiero sumar efectivo a mi caja (`mode: add`), para registrar una asignación.
13. Como corresponsal, quiero fijar el total de mi caja (`mode: set`), para corregir un descuadre.
14. Como corresponsal, quiero ver el antes y el después del efectivo en la respuesta.
15. Como corresponsal, quiero que fijar un total por debajo del efectivo reservado responda `422 insufficient_cash` con `shortfall`.
16. Como corresponsal, quiero que un ajuste sin variación responda `422 no_change`.
17. Como corresponsal, quiero que el motivo por defecto sea "Asignación de efectivo" o "Ajuste de efectivo" si no escribo uno.
18. Como corresponsal, quiero que repetir el ajuste con la misma `Idempotency-Key` me devuelva el mismo ajuste sin sumar dos veces.

### Desglose en vivo

19. Como corresponsal, quiero un desglose de depósito o retiro mientras escribo el monto, para mostrar tarifa, neto y cobrado.
20. Como corresponsal, quiero que "valor redondo" haga que el usuario reciba el monto exacto y pague la tarifa aparte.
21. Como corresponsal, quiero ver cupo, efectivo y comisión después de la operación.
22. Como corresponsal, quiero que el desglose responda 200 con `violations` (`amount_too_low`, `insufficient_quota`, `insufficient_cash` con `shortfall`), para deshabilitar el botón sin tratarlo como error.
23. Como corresponsal, quiero que la tarifa venga de la tarifa vigente ($1.000: 500 Relia y 500 corresponsal).

### Llaves

24. Como corresponsal, quiero validar el celular del usuario y ver su billetera enmascarada.
25. Como corresponsal, quiero que un celular sin llave responda `404 key_not_found`, para ofrecer la invitación.
26. Como corresponsal, quiero invitar por WhatsApp a un celular sin llave y recibir `waLink` y `message`.
27. Como quien presenta, quiero que la invitación aparezca en el teléfono simulado como `invitacion_wallet`, con el enlace a la página pública y el celular precargado.

### Vinculación pública

28. Como usuario, quiero escribir mi celular y mi payment pointer y recibir un código por SMS, sin iniciar sesión.
29. Como usuario, quiero que el pointer se valide en el riel antes de enviar el código, para que una billetera inexistente responda `422 wallet_not_found`.
30. Como usuario, quiero que un celular con llave responda `409 key_already_linked`, porque la llave es permanente.
31. Como usuario, quiero que una billetera ya vinculada a otro celular responda `409 wallet_already_linked`, sin revelar a cuál.
32. Como Relia, quiero que el celular o la billetera de un corresponsal o de Relia respondan `409 phone_reserved`.
33. Como usuario, quiero un OTP de 6 dígitos que venza en 5 minutos, con 5 intentos.
34. Como usuario, quiero que "Reenviar código" invalide el desafío anterior.
35. Como usuario, quiero que un código malo responda `422 invalid_otp` con `attemptsRemaining`.
36. Como usuario, quiero `otp_expired` y `otp_attempts_exceeded` cuando corresponda.
37. Como usuario, quiero que el código correcto cree la llave y devuelva mi pointer completo y `linkedAt`.
38. Como Relia, quiero que, si otro flujo vinculó el celular o la billetera durante el desafío, verificar responda el mismo 409 que al iniciar.
39. Como Relia, quiero limitar intentos con `429 rate_limited` y `Retry-After`.

### Depósito

40. Como corresponsal, quiero crear un depósito al confirmar que recibí el efectivo, para que Relia pague el neto a la billetera del usuario.
41. Como corresponsal, quiero que el depósito reserve `chargedAmount` de cupo mientras está pendiente.
42. Como corresponsal, quiero `422 insufficient_quota` con `shortfall` si no me alcanza el cupo.
43. Como corresponsal, quiero `404 key_not_found` si el celular no tiene llave.
44. Como corresponsal, quiero `503 rail_unavailable` sin crear nada si el riel no acepta el pago, para reintentar con la misma clave.
45. Como corresponsal, quiero que el depósito se cree en `pending` (202) y yo consulte su estado cada 1–2 s.
46. Como corresponsal, quiero que al completarse baje el cupo, suban el efectivo y la comisión, y se congele el recibo, todo en una transacción.
47. Como corresponsal, quiero que un pago de riel `failed` deje la operación `failed` con `payment_failed` y libere la reserva.
48. Como usuario, quiero un SMS simulado `aviso_deposito` al completarse el depósito.

### Retiro

49. Como corresponsal, quiero crear un retiro que reserve el neto de mi efectivo y le cobre `chargedAmount` a la billetera del usuario.
50. Como corresponsal, quiero recibir `approvalUrl` y `expiresAt` (5 min), para mostrar el contador.
51. Como usuario, quiero recibir la `approvalUrl` por SMS simulado (`solicitud_pago`).
52. Como corresponsal, quiero `422 insufficient_cash` con `shortfall` si no me alcanza el efectivo.
53. Como corresponsal, quiero que el retiro pase a `completed` cuando el usuario aprueba y el pago llega, y entonces suban el cupo y la comisión y baje el efectivo.
54. Como corresponsal, quiero `failed`/`customer_rejected`, `expired`/`request_expired` o `cancelled`/`request_cancelled` según el estado del pago de riel.
55. Como corresponsal, quiero cancelar un retiro pendiente antes de la aprobación, y que después responda `409 operation_not_cancellable`.
56. Como corresponsal, quiero confirmar la entrega del efectivo de un retiro `completed`, para dejarla en auditoría sin tocar saldos, de forma idempotente.
57. Como corresponsal, quiero que confirmar la entrega de un retiro no completado responda `409 operation_not_completed`.
58. Como Relia, quiero que el backend venza el retiro a los 5 min aunque el proxy no lo haya reportado, porque el backend es dueño del plazo de la operación.

### Carga de cupo

59. Como corresponsal, quiero cargar cupo por Bre-B y recibir un `qrPayload` simulado que vence en 10 min.
60. Como corresponsal, quiero cargar cupo por Wallet: Relia le cobra a mi billetera registrada, con `approvalUrl` y 5 min de plazo.
61. Como corresponsal, quiero que la carga completada suba mi cupo sin tarifa (`fee` en 0).
62. Como corresponsal, quiero cancelar una carga pendiente.
63. Como corresponsal, quiero que la carga aparezca en "Mi billetera" del teléfono simulado con mi celular.

### Operaciones, historial y recibos

64. Como corresponsal, quiero que `GET /operations/{operationId}` refleje el último estado del pago de riel, consultando el proxy como máximo una vez por segundo por pago.
65. Como Relia, quiero un barrido de fondo cada 15 s que avance las operaciones y liquidaciones pendientes aunque nadie las consulte.
66. Como Relia, quiero descartar lecturas del proxy con `version` menor a la ya procesada.
67. Como corresponsal, quiero mi historial unificado (operaciones, ajustes y liquidaciones), el más reciente primero, paginado con cursor.
68. Como corresponsal, quiero filtrar el historial por `kind` y `status`.
69. Como corresponsal, quiero que las entradas no completadas muestren antes = después con el último saldo conocido.
70. Como corresponsal, quiero que las liquidaciones pagadas aparezcan como `completed` y las anuladas como `cancelled`.
71. Como corresponsal, quiero ver el recibo congelado de un depósito o retiro `completed`, con `code`, `contentHash` y los datos enmascarados.
72. Como corresponsal, quiero que desde la segunda emisión `isCopy` sea true, para que el documento diga "COPIA".
73. Como corresponsal, quiero que cada emisión (`screen`, `print`, `pdf`, `sms`, `whatsapp`) quede registrada.
74. Como corresponsal, quiero `409 receipt_not_available` en cargas y `409 operation_not_completed` en operaciones no completadas.
75. Como corresponsal, quiero enviar el recibo por SMS al usuario y que aparezca en el teléfono simulado (`comprobante`).
76. Como usuario, quiero que mi celular y mi billetera salgan enmascarados en toda respuesta al corresponsal y al admin.

### Admin

77. Como admin, quiero ver todos los corresponsales con métricas y los totales de la red en una sola llamada.
78. Como admin, quiero que los montos movidos cuenten solo depósitos y retiros completados, con el monto solicitado.
79. Como admin, quiero ver las liquidaciones de hoy (America/Bogota) con `GET /settlements?from=`.
80. Como admin, quiero filtrar liquidaciones por fecha, corresponsal y estado.
81. Como admin, quiero pagar toda la comisión acumulada de un corresponsal a su billetera, con el monto fijo desde que se crea.
82. Como admin, quiero que el período vaya desde el día siguiente a la última liquidación pagada (o desde la primera operación) hasta hoy.
83. Como admin, quiero `409 settlement_in_progress` si ya hay una pendiente, y `422 no_commission` si la comisión es $0.
84. Como admin, quiero `503 rail_unavailable` sin crear nada si el riel no acepta el pago (por ejemplo, sin permiso activo).
85. Como admin, quiero que la liquidación pase a `paid`, o a `voided` con `voidReason`, según el pago de riel.
86. Como corresponsal, quiero un SMS simulado `aviso_comision` cuando me pagan la comisión.
87. Como admin, quiero que una billetera simulada (`walletSimulated`) se liquide sin pasar por el riel.
88. Como Relia, quiero que el admin reciba `403 forbidden` al crear depósitos, retiros, cargas o ajustes, al cancelar y al confirmar entregas.
89. Como corresponsal, quiero ver solo mis liquidaciones, ignorando el filtro `agentId`.

### Modo demo

90. Como quien presenta, quiero que `/demo/*` funcione sin login con `DEMO_MODE=true` y responda 404 sin el flag.
91. Como quien presenta, quiero simular que llegó el pago Bre-B de una carga pendiente.
92. Como quien presenta, quiero ver la bandeja del teléfono simulado, global o por celular, con `unreadCount`.
93. Como quien presenta, quiero marcar como leídos los mensajes al abrir el teléfono.
94. Como quien presenta, quiero ver las solicitudes de pago pendientes de un celular con su `approvalUrl`, para aprobar en la wallet de prueba.
95. Como quien presenta, quiero que "El próximo pago falla" deje la siguiente operación o liquidación en `failed`/`payment_failed` sin llamar al riel, y que se apague sola.
96. Como quien presenta, quiero vencer ya un retiro o una carga pendiente, cancelando en el riel.
97. Como quien presenta, quiero reiniciar la demo: cancelar cobros pendientes en el riel, vaciar los datos y volver a sembrar 4 corresponsales (Rosa en ceros), el 3000000001 vinculado a `jeffdev` y el 3000000002 sin llave.

### Transversal

98. Como frontend, quiero todos los errores en `application/problem+json` con `code` del enum del contrato.
99. Como frontend, quiero que toda creación sea idempotente por `Idempotency-Key`: la misma clave con el mismo cuerpo devuelve el recurso existente y con otro cuerpo responde `409 idempotency_conflict`.
100. Como frontend, quiero que un 4xx o un 503 al crear no deje nada creado, para reintentar con la misma clave.
101. Como Relia, quiero que cada cambio de estado y cada respuesta del riel queden en las bitácoras de solo inserción.
102. Como frontend, quiero que las fechas salgan en RFC 3339 UTC y el dinero en enteros COP.

## Implementation Decisions

- **Alcance**: solo el backend .NET. El proxy Node es otra spec. El backend consume el proxy solo por HTTP y según `rail-proxy.openapi.yaml`.
- **Contrato como fuente de verdad**: rutas, cuerpos, códigos de estado y `code` de error salen tal cual de `relia-api.openapi.yaml`. Si algo del contrato no se puede implementar, se cambia el contrato primero y después el código.
- **Persistencia**: PostgreSQL 16 con el esquema v0.2 sin rediseñarlo. Las triggers, enums y restricciones del esquema hacen cumplir sus reglas. La aplicación hace, en una transacción con bloqueo de la fila de `cupos`, lo que el esquema deja a su cargo: actualizar cupo, comisión y efectivo, escribir el movimiento de cupo y congelar el recibo. Los nombres de la base quedan en español y los de la API en inglés, y el mapeo vive en la capa de persistencia.
- **Módulos** (módulos profundos, interfaz pequeña):
  - *Auth*: login con contraseñas hasheadas de `corresponsales` y `admins`, emisión de JWT con `role` y `agentId`, y una política de autorización por rol y propiedad del corresponsal.
  - *Ledger de saldos*: único lugar que cambia cupo, comisión y efectivo. Calcula reservas a partir de las pendientes y aplica la transición a `completed`. El desglose de vista previa usa las mismas reglas.
  - *Tarifas y desglose*: función pura de (tipo, monto, valor redondo, tarifa vigente) → `fee`, `netAmount`, `chargedAmount`.
  - *Operaciones*: crea depósito, retiro y carga, guarda el pago (`pagos`) y llama al riel. Mantiene la máquina de estados de la operación y aplica la tabla de traducción del pago de riel a operación (`rejected` → `failed/customer_rejected`, etc.).
  - *Cliente de riel* (`IRailClient`): resolver pointer, crear pago, consultar pago y cancelar pago. Convierte COP enteros a `{ value: pesos × 100, assetCode: "COP", assetScale: 2 }`, usa `X-Api-Key` y manda como `Idempotency-Key` el id del pago del backend (`pagos.id`). Es la única costura hacia afuera.
  - *Sincronizador*: refresco al leer (como máximo una vez por segundo por pago) y barrido de fondo cada 15 s. Descarta lecturas con `version` menor a la procesada y vence en el backend las operaciones pasadas de `expiresAt` (5 min Wallet, 10 min Bre-B), cancelando en el riel.
  - *Llaves y vinculación*: búsqueda de llave, invitación, desafíos OTP (6 dígitos, 5 min, 5 intentos, reenvío que invalida), unicidad de celular y billetera, reserva de los de corresponsales y Relia, y rate limit con `429`.
  - *Recibos*: recibo congelado con código aleatorio de 12 hex y SHA-256 del contenido, más las emisiones por canal con `isCopy`.
  - *Liquidaciones*: monto y período fijos al crear, una pendiente por corresponsal, `pending → paid | voided` y el atajo `walletSimulated`.
  - *Mensajería simulada*: escribe en `mensajes_simulados` (otp, invitacion_wallet, aviso_deposito, solicitud_pago, comprobante, aviso_comision).
  - *Modo demo*: rutas `/demo/*` registradas solo con `DEMO_MODE=true`, el fallo forzado `failNextPayment` (de un solo uso), "vencer ya" y el reinicio con siembra.
  - *Idempotencia*: guarda clave, hash del cuerpo y respuesta por ruta y actor. Con otro cuerpo responde `409 idempotency_conflict`. Si la respuesta fue 4xx o 503, no se guarda nada.
  - *Errores*: un único mapeo de excepciones de dominio a `problem+json` con `code`, `shortfall`, `attemptsRemaining` y `errors` por campo.
- **Reloj inyectable** (`TimeProvider`) para todos los plazos (OTP, vencimiento de solicitudes, "hoy" en America/Bogota).
- **Configuración**: cadena de conexión a Postgres, URL y API key del proxy, secreto JWT, `DEMO_MODE` y URL base de la página pública (para el enlace de invitación).
- **Proxy no disponible o sin permiso activo** al crear (`rail_unavailable` o `relia_grant_unavailable` del proxy) → `503 rail_unavailable` y rollback total. Si el proxy deja de responder con la operación ya creada, la operación sigue `pending` y el sincronizador reintenta.
- **Enmascarado**: la base guarda datos completos y el backend enmascara al responder (`300 *** 0001`, `$ilp...test.dev/us***`). La única excepción es la respuesta de `verify`, que devuelve el eco completo.
- Se elimina la plantilla `WeatherForecast`.

## Testing Decisions

- **Una costura arriba**: pruebas de integración HTTP de la Relia API con `WebApplicationFactory`. Cada prueba llama endpoints como lo haría el frontend y verifica solo respuestas HTTP (estado, cuerpo, `code`) y efectos visibles por otros endpoints (saldos en `GET /agents/{id}`, historial, bandeja de `/demo/messages`). No se prueban servicios internos, repositorios ni SQL directamente.
- **Costura abajo**: `IRailClient` se reemplaza por un doble en memoria controlable desde la prueba (fijar el estado de un pago, simular 503, `version`, registrar llamadas y claves de idempotencia). El `TimeProvider` falso hace avanzar el tiempo para OTP y vencimientos.
- **Base de datos real**: PostgreSQL en Testcontainers con el esquema v0.2 aplicado, para que las triggers y restricciones del esquema también queden probadas. Se limpian los datos entre pruebas (o se usa `POST /demo/reset`).
- **Qué se cubre como mínimo**: cada paso del guion de FLUJOS.md §12 de punta a punta; cada fila de la tabla de traducción de estados; cada `code` de error que declara el contrato; la idempotencia (repetición, conflicto, error sin creación); las reglas de rol (`403` del admin al operar y del agente con un corresponsal ajeno); y las reservas y los antes/después de saldos.
- **Conformidad con el contrato**: una prueba valida respuestas contra `relia-api.openapi.yaml` (por ejemplo, validación de esquema JSON de los cuerpos de respuesta en las pruebas de integración).
- **Prior art**: no hay pruebas en el backend todavía. Este trabajo crea el proyecto de pruebas. El frontend tiene specs de servicios (`*.service.spec.ts`) que muestran qué respuestas espera de la API.

## Out of Scope

- El Rail Proxy Node y todo lo de Open Payments (grants, quotes, el permiso permanente de Relia): otra spec.
- Cambios al frontend.
- Bre-B real, SMS y WhatsApp reales, y verificación de dueño de la billetera.
- Conciliación cupo vs. riel y saldo de la billetera de Relia en el admin (sin especificar en el mapa).
- Reemplazar la billetera de una llave y consultar recibos con código + OTP.
- Despliegue, observabilidad y rediseño del esquema.

## Further Notes

- **Ticket 12 abierto** ([moneda al vincular llave](../../contratos-api/issues/12-moneda-al-vincular-llave.md)): el contrato hoy no rechaza una billetera que no sea COP. Esta spec implementa el contrato tal cual. Si el ticket se resuelve con `422 wallet_currency_unsupported`, se agrega aquí como cambio aparte.
- **Ticket 11 abierto** ([billetera para el celular sin llave](../../contratos-api/issues/11-billetera-para-el-celular-sin-llave.md)): el paso 6 del guion necesita una cuarta billetera COP. No bloquea la implementación, solo la demo en vivo.
- Antes de presentar, el operador aprueba el permiso permanente de Relia en el proxy ([FLUJOS.md §0](../../../../contracts/FLUJOS.md#0-preparación-del-riel-operador-una-vez)). Sin él, depósitos y liquidaciones responden 503.
- Vocabulario según [GLOSSARY.md](../../../../GLOSSARY.md): corresponsal (`agent`), usuario (`customer`), llave (`key`), cupo (`quota`), efectivo en caja (`cashOnHand`), liquidación (`settlement`).
