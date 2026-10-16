# Contrato de riel: recursos, estados y webhook del proxy

Type: grilling
Status: open
Blocked by: 01, 02, 03, 09
Labels: wayfinder:grilling

## Question

Con los hechos de Open Payments ya investigados, ¿cuál es el contrato `rail-proxy.openapi.yaml` que el backend consume y que un adaptador Bre-B también podría implementar?

- Recursos: ¿un solo `/payments` con `direction: send | request`, o `/payouts` y `/payment-requests` por separado? ¿Resolver o validar un payment pointer es su propio endpoint?
- Máquina de estados del pago de riel y su relación con la operación del backend. ¿Quién mapea `rejected` a `failed` con motivo?
- Forma del webhook (evento, payload, firma HMAC con la clave compartida, reintentos) y registro del callback URL (¿por pago o fijo por configuración?).
- Cancelación y vencimiento: ¿el backend envía `expiresAt` al crear o solo llama `cancel`?
- Dinero `{ value, assetCode, assetScale }` y manejo de moneda si la red de prueba no tiene COP (según el research de COP).
- Qué datos del riel se devuelven para el comprobante y la conciliación (ids externos, montos enviados o recibidos).
- Ganchos de demo para forzar fallos: ¿viven en el proxy o el backend los simula sin tocar el riel?

Produce: `contracts/rail-proxy.openapi.yaml`.
