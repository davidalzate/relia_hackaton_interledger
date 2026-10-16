# Contrato de riel: recursos, estados y webhook del proxy

Type: grilling
Status: resolved
Blocked by: 01, 02, 03, 09
Labels: wayfinder:grilling

## Question

Con los hechos de Open Payments ya investigados, ¿cuál es el contrato `rail-proxy.openapi.yaml` que el backend consume y que un adaptador Bre-B también podría implementar?

- Recursos: ¿un solo `/payments` con `direction: send | request`, o `/payouts` y `/payment-requests` por separado?
- **Requisito ya decidido**: un endpoint para resolver o validar un payment pointer (existe o no, `assetCode`, `assetScale`). La vinculación de llave lo usa al iniciar ([API Relia: vinculación pública de llave con OTP](05-api-vinculacion-de-llave.md)).
- Máquina de estados del pago de riel y su relación con la operación del backend. ¿Quién mapea `rejected` a `failed` con motivo?
- Forma del webhook (evento, payload, firma HMAC con la clave compartida, reintentos) y registro del callback URL (¿por pago o fijo por configuración?).
- Cancelación y vencimiento: ¿el backend envía `expiresAt` al crear o solo llama `cancel`?
- Dinero `{ value, assetCode, assetScale }` y manejo de moneda si la red de prueba no tiene COP (según el research de COP).
- Qué datos del riel se devuelven para el comprobante y la conciliación (ids externos, montos enviados o recibidos).
- El esquema ([relia_schema_hackathon_v0.2.sql](../../../relia_schema_hackathon_v0.2.sql), tabla `pagos`) ya define lo que el backend guarda de cada pago de riel. El contrato debe devolver al menos eso: dirección `entrante | saliente`, pointers de origen y destino, URLs de incoming, quote y outgoing, `url_aprobacion`, monto debitado y recibido `{ value, assetCode, assetScale }`, `vence_en` y error del riel. El esquema guarda `grant_continue_uri` y el token cifrado del lado del backend, lo cual choca con "solo el proxy tiene las llaves GNAP" ([ADR 0001](../../../adr/0001-proxy-como-contrato-de-riel.md)). Hay que decidir si esas columnas se eliminan o quedan sin usar.
- Ganchos de demo para forzar fallos: ¿viven en el proxy o el backend los simula sin tocar el riel?

Produce: `contracts/rail-proxy.openapi.yaml`.

## Answer

Resuelto en grilling (3 rondas, 2026-10-16). Asset: [contracts/rail-proxy.openapi.yaml](../../../../contracts/rail-proxy.openapi.yaml) (Redocly: válido, 3 avisos cosméticos).

**Billeteras reales** (verificadas con `curl`, todas COP con `assetScale: 2` y el mismo servidor de autorización): Relia `$ilp.interledger-test.dev/relia-test1`, Rosa `$ilp.interledger-test.dev/rosa-test`, usuario demo `$ilp.interledger-test.dev/jeffdev`. La llave privada de Relia va en una variable de entorno en base64.

- **Recursos**: un solo `POST /payments` con `direction: outgoing | incoming` (visto desde Relia) y `counterparty`; la otra punta es siempre la billetera de Relia. También `GET /payments/{id}`, `POST /payments/{id}/cancel`, `GET /wallet-addresses?pointer=` (lo usa la vinculación) y `GET /health`.
- **Estados**: `pending_approval` (solo `incoming`) → `processing` → `completed`, más `failed`, `rejected`, `expired` y `cancelled`. No hay `unknown`: si el riel no confirma, el pago sigue en `processing`. El backend traduce `rejected` a una operación `failed` con `customer_rejected`.
- **Aprobación**: el pagador aprueba en su billetera, que hace de app bancaria simulada con Open Payments, y **no vuelve a ninguna página de Relia**. El proxy pide la aprobación sin redirect de vuelta y consulta el riel para saber si fue aprobada o rechazada. El backend entrega `approval.url` por SMS simulado. La aprobación es agnóstica al riel: `approval: { method: redirect | qr, url | payload }`.
- **Sin webhooks**: el backend consulta `GET /payments/{id}` al leer una operación pendiente (como mucho 1 vez por segundo por pago) y en un barrido de fondo cada 15 s. El proxy responde con su último estado conocido y descarta lecturas viejas por `version`.
- **Vencimiento**: el backend envía `expiresAt` y el proxy lo hace cumplir con su propio temporizador, porque Open Payments no lo reporta. El backend conserva el suyo como respaldo y llama a `cancel`. Solo se cancela antes de la aprobación; después responde `409 payment_not_cancellable`.
- **Dinero**: `receiveAmount` fijo, así que el destino recibe exacto y el costo del riel lo absorbe el pagador. El pago reporta `debitAmount` y `receivedAmount`. COP escala 2: $50.000 = `"5000000"`.
- **Permiso permanente de Relia**: tag `operator` (`GET` y `POST /relia-grant`), con tope por intervalo y aprobación del operador. El backend no lo consume y el panel admin no lo muestra. Si no hay permiso activo, crear un `outgoing` responde `503 relia_grant_unavailable`.
- **Idempotencia**: `Idempotency-Key` es el `pagos.id` del backend.
- **Esquema**: `grant_continue_uri` y `grant_continue_token_cifrado` **se quedan, pero no se usan**; el backend guarda el id del pago en el proxy y las URLs públicas del riel.
- **Demo**: el proxy no tiene ganchos de demo; el backend simula los fallos forzados.
