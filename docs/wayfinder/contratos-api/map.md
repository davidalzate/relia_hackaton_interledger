# Mapa: contratos OpenAPI de Relia (backend .NET y proxy de riel)

Labels: wayfinder:map

## Destination

Dos archivos OpenAPI 3.1 en el repo que pasan un linter (Spectral o Redocly) y cubren el guion de demo de [prototype.md §12](../../prototype.md): `contracts/relia-api.openapi.yaml` (frontend → backend .NET) y `contracts/rail-proxy.openapi.yaml` (backend → proxy Node, adaptador de Open Payments). Con ellos, front, backend y proxy se construyen en paralelo.

## Notes

- **Dominio**: Relia, efectivo digital en corresponsales. Fuentes: [business_model.txt](../../business_model.txt), [prototype.md](../../prototype.md), [detalle_pantallas.md](../../detalle_pantallas.md). Vocabulario en [GLOSSARY.md](../../../GLOSSARY.md); cada sesión lo consulta y lo actualiza (skills `grilling` y `domain-modeling`).
- **Ejecución dentro del mapa**: como el destino es el contrato, al resolver cada ticket de diseño se escribe su fragmento en el YAML que le corresponde. Esto anula el "plan, don't do" por defecto solo para los archivos de `contracts/`.
- **Tracker**: markdown local, un archivo por ticket en [issues/](./issues/), con líneas `Type:`, `Status: open|claimed|resolved` y `Blocked by:`. Se migra a GitHub Issues cuando `gh` tenga sesión.
- **Decisiones del trazado (sesión del 2026-10-16)**, válidas para todos los tickets:
  - Pagos reales en la red de prueba de Interledger: depósito, retiro, carga por Wallet y pago de comisión. Simulados: Bre-B (simulador dentro del backend), SMS/OTP/WhatsApp y la verificación de dueño de la billetera.
  - El proxy es un **adaptador de riel** con contrato agnóstico ([ADR 0001](../../adr/0001-proxy-como-contrato-de-riel.md)). Solo el proxy tiene las llaves GNAP.
  - Cobrar a una billetera devuelve una `approvalUrl`. El backend la entrega por SMS simulado y el frontend la muestra como enlace o QR.
  - ~~Del proxy al backend: webhook~~ → **Sin webhooks** (cambiado en el contrato de riel): el backend consulta `GET /payments/{id}` del proxy, y el proxy consulta Open Payments por su cuenta.
  - Del frontend al backend: polling a `GET /operations/{id}` cada 1 a 2 s mientras la operación esté `pending`. Sin SSE.
  - Hay dos máquinas de estados. El backend es dueño de la **operación** (`pending → completed | failed | expired | cancelled`) y de los plazos de 5 y 10 min. El proxy es dueño del **pago de riel** y recibe `cancel`.
  - `Idempotency-Key` en toda creación de ambos APIs. Hacia el proxy es el id del **pago** del backend (`pagos.id`), no el de la operación (cambiado en el contrato de riel).
  - Autenticación: JWT bearer con rol `agent` o `admin`. La vinculación de llave es pública. Backend → proxy usa `X-Api-Key`.
  - Errores en `application/problem+json` (RFC 9457) con `code` en un enum estable. El texto en español lo pone el frontend.
  - Dinero: en el API de Relia, enteros en pesos COP. En el contrato de riel, `{ value, assetCode, assetScale }`. El backend convierte.
  - N corresponsales genéricos (`/agents/{agentId}`). La siembra es interna del backend y solo existe `POST /demo/reset`.
  - El grupo `demo` vive en el backend (tag `demo`) y un flag de entorno lo apaga.
  - Identificadores en inglés y textos en español.

## Decisions so far

<!-- una línea por ticket resuelto: - [título](issues/NN-slug.md): gist -->

- [Open Payments: cómo paga Relia desde su propia billetera sin aprobar cada pago](issues/01-op-pago-saliente-desde-relia.md): exige un grant interactivo único con tope por intervalo, aprobado por un operador; después el proxy paga solo y rota el token; el éxito se confirma consultando el incoming payment.
- [Open Payments: cómo cobra Relia a la billetera de otra persona con aprobación interactiva](issues/02-op-cobro-interactivo.md): `approvalUrl` = `interact.redirect`; Relia crea todo con sus llaves; finish y polling se excluyen; el vencimiento no se ve en el polling (temporizador propio de 5 min); solo se cancela antes de crear el outgoing payment.
- [Red de prueba de Interledger: ¿hay billeteras en COP y cómo se fondean?](issues/03-testnet-cop.md): COP probablemente disponible (GateHub sandbox, mayo 2026), por confirmar registrándose; escala esperada 2; una cuenta por moneda y por usuario; plan B en USD con tasa fija en el backend.
- [API Relia: recursos de operaciones, saldos e historial del corresponsal](issues/04-api-operaciones-y-saldos.md): alineado al esquema SQL v0.2. Operación = depósito, retiro o carga; la liquidación y el ajuste de efectivo van aparte; historial unificado con `kind`; vista previa en el backend; reservas calculadas de las pendientes; recibo congelado con emisiones. Escrito en `contracts/relia-api.openapi.yaml`.
- [Crear las billeteras de prueba y confirmar COP](issues/09-crear-billeteras-de-prueba.md): COP disponible en la wallet de prueba; tres billeteras creadas, Relia fondeada y developer keys generadas fuera del repo. Se descarta el plan B en USD.
- [Contrato de riel: recursos, estados y webhook del proxy](issues/06-contrato-de-riel.md): un solo `/payments` (`outgoing | incoming`); el pagador aprueba en su billetera sin volver a Relia y el proxy detecta el resultado; sin webhooks, el backend consulta; el proxy hace cumplir `expiresAt`; monto fijo del lado que recibe; permiso permanente de Relia en un tag `operator`; proxy sin ganchos de demo. Escrito en `contracts/rail-proxy.openapi.yaml`.
- [API Relia: panel admin y pago de comisión](issues/07-api-admin.md): `GET /agents` con filas y totales de la red en una llamada; liquidación de toda la comisión con monto congelado, una pendiente por corresponsal, `pending | paid | voided` sin anulación manual; el admin lee y liquida pero no opera.
- [API Relia: endpoints del Modo demo](issues/08-api-demo.md): `/demo/*` sin login detrás de `DEMO_MODE`; simulador Bre-B; bandeja global del teléfono; "Mi billetera" solo enlaza a la aprobación real; un único fallo forzado más la acción "vencer ya"; reinicio que no devuelve dinero real; corresponsales de ejemplo con billetera simulada.
- [API Relia: vinculación pública de llave con OTP](issues/05-api-vinculacion-de-llave.md): llave inmutable (gana el esquema, sin reemplazo); celular y wallet únicos y separados de los del corresponsal; pointer validado en el riel; `POST /key-links` + `verify`; OTP de 5 min y 5 intentos; `429` declarado.

## Not yet specified

- **Conciliación**: si el admin necesita ver el saldo de la billetera de Relia o una conciliación cupo vs. riel. Aún no está claro si cabe en el demo.

## Out of scope

- Implementación del backend, el proxy o el frontend. Este mapa termina en los contratos.
- Verificación de dueño de la billetera, Bre-B real, SMS y WhatsApp reales: fuera del demo según el modelo de negocio.
- Persistencia, despliegue y observabilidad. El esquema [relia_schema_hackathon_v0.2.sql](../../relia_schema_hackathon_v0.2.sql) ya existe y los contratos se alinean a él, pero no lo rediseñan.
- Reemplazar la billetera vinculada a un celular: el esquema hace la llave inmutable y no está en el guion (decidido en [API Relia: vinculación pública de llave con OTP](issues/05-api-vinculacion-de-llave.md)).
- Consulta de recibos por el usuario con código + OTP: el esquema la soporta, pero no está en el prototipo ni en el guion de demo (decidido en [API Relia: recursos de operaciones, saldos e historial del corresponsal](issues/04-api-operaciones-y-saldos.md)).
