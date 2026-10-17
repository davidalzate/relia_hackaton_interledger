# 15: Ensayo del guion completo contra la red de prueba (HITL)

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** antes de presentar, una persona corre de punta a punta el guion de FLUJOS.md §12 con la colección de Postman de la Relia API. El ensayo usa el backend, la base en Docker recién creada y el proxy de riel real en la red de prueba. Anota en este ticket lo que observa. HITL: hace falta aprobar y rechazar en la wallet de prueba.

**Blocked by:** 08 (Recibos de depósito y retiro), 14 (Modo demo: fallos forzados, "vencer ya" y reinicio)

**Status:** done (ensayado el 2026-10-17 contra la red de prueba. Las llamadas se hicieron con un script, no con la colección de Postman: ver notas)

- [x] Arranque en limpio: borrar el volumen, `docker compose up`, arrancar el backend con `DEMO_MODE=true` e iniciar sesión como Rosa.
- [x] El permiso permanente de Relia está aprobado en el proxy (FLUJOS §0).
- [x] Los 8 pasos del guion pasan, de `POST /demo/reset` al pago de comisión de Rosa, o cada falla queda anotada con su ticket.
- [x] Cada fila de la tabla de traducción de estados se observó al menos una vez: aprobar, rechazar, dejar vencer, cancelar y `failNextPayment`.
- [x] Cada `code` de error del contrato se vio al menos una vez o queda anotado como no alcanzable a mano.
- [x] Las respuestas coinciden con `relia-api.openapi.yaml` (estado, `content-type` y forma del cuerpo). Las diferencias se corrigen en el código o abren un cambio al contrato.
- [x] `v_agent_balance_check` da los tres descuadres en 0 al final del guion.
- [x] Queda anotado si el paso 6 se pudo correr: necesita una billetera COP libre (ticket 11 de contratos-api).

## Resultado del ensayo (2026-10-17, ~05:00–05:15 UTC)

**Montaje**: volumen borrado y `docker compose up` (16 tablas, base vacía). El backend arrancó con `DEMO_MODE=true` y sembró la base. El proxy real corrió sobre `$ilp.interledger-test.dev/relia-test1`, la misma billetera que `Relia:WalletAddress`. Las llamadas se hicieron con un script que guarda cada respuesta; la colección de Postman no se corrió.

**Hallazgo de montaje**: el `dist/` del proxy estaba compilado antes de la ruta `/relia-grant` (`POST /relia-grant` → `404`). Hubo que hacer `npm run build` antes de `npm start`. Antes de presentar, compilar siempre el proxy (o usar `npm run dev`).

**Permiso de Relia**: `POST /relia-grant` (tope $1.000.000 por día, desde el 2026-10-17 en Bogotá) → aprobado por el operador → `active`. Antes de aprobarlo, un depósito respondió `503 rail_unavailable` sin crear nada.

**Guion** (todo contra el riel real, salvo Bre-B y los fallos forzados):

| # | Resultado |
|---|---|
| 0 | `POST /demo/reset` → `204` |
| 1 | Rosa en ceros; historial vacío |
| 2 | Efectivo +$300.000 → `201` |
| 3 | Carga Bre-B $500.000 (OP-000014) → simulada → `completed`, cupo 500.000 |
| 4 | Depósito $50.000 a `jeffdev` (OP-000017) → `completed` en ~3 s, recibo congelado `FCD6860A5F0F` |
| 5 | Retiro $100.000 (OP-000018) aprobado por `jeffdev` → `completed`; entrega confirmada (repetirla → `200`) |
| 6 | 3000000002 → `404`; invitación `201` y WhatsApp en la bandeja; vinculado a `$ilp.interledger-test.dev/jeffdev1` con OTP; retiro $20.000 (OP-000023) aprobado por `jeffdev1` → `completed` |
| 7 | Historial completo; recibo del retiro; reimpresión del depósito (`isCopy: true`) y envío por SMS → `202` |
| 8 | Admin: `GET /agents`, `GET /settlements?from=hoy`, detalle de Rosa. Liquidación de Rosa $1.500 (LQ-000001) por el riel → `paid` en segundos, comisión 0, liquidada 1.500, SMS `aviso_comision` |

**Paso 6**: se pudo correr con la billetera libre `jeffdev1`, que aportó quien ensayó. Sigue abierto el ticket 11 de contratos-api: falta decidir qué billetera fija usa la demo para el 3000000002.

**Tabla de traducción de estados** (todas observadas):
- `processing` → `pending`: depósito.
- `pending_approval` → `pending`: retiros.
- aprobar → `completed`: OP-000018 y OP-000023.
- rechazar → `failed`/`customer_rejected`: OP-000019.
- dejar vencer → `expired`/`request_expired`: OP-000020, por el barrido; el proxy lo reporta `expired`.
- cancelar → `cancelled`/`request_cancelled`: OP-000016, cancelado en el proxy.
- "vencer ya" → `expired`: OP-000022, con el pago `cancelled` en el proxy.
- `failNextPayment` → `failed`/`payment_failed`: OP-000021.
- liquidaciones: `paid` por el riel (LQ-000001), `paid` simulada para Luis (LQ-000002) y `voided` forzada para Diana (LQ-000003).
- **No observado**: un `failed` real del riel (`payment_failed` sin forzar). No hay forma de provocarlo a mano.

**Códigos de error**: los 25 del enum `ErrorCode` aparecieron, cada uno con su estado y `problem+json`.
- `amount_too_low` solo se ve en `violations` del desglose. Al crear no se alcanza: el DTO exige `requestedAmount` ≥ 1001, así que antes responde `validation_error`, y la regla corta en ≤ $1.000.
  - Al probarlo con $1.001 se creó un retiro real (OP-000016), que se canceló.
- `otp_expired` apareció al verificar un desafío que el reenvío había anulado.
- `otp_attempts_exceeded`: el quinto código malo responde `invalid_otp` con `attemptsRemaining: 0`; el sexto, `otp_attempts_exceeded`.
- `settlement_in_progress` salió con dos `POST` simultáneos.
- `rate_limited` salió en el intento 21 a `/key-links`, con `Retry-After: 60`.

**Contrato**: 105 respuestas validadas contra `relia-api.openapi.yaml` (estado documentado, `content-type`, esquema del cuerpo y `Location`), 51 combinaciones de ruta y estado. Hubo una diferencia, que se corrigió en el contrato: `POST /keys/{phone}/invitations` responde `422 validation_error` sin `Idempotency-Key` y el contrato no lo documentaba. Se agregó el `422`.

**Cierre**: `v_agent_balance_check` en 0 para los cuatro corresponsales. Ni el backend ni el proxy registraron errores (`fail:` / nivel 50).

**Se gastó en la red de prueba**:
- de Relia a `jeffdev`: $49.000 (depósito);
- de Relia a `rosa-test`: $1.500 (comisión);
- de `jeffdev` a Relia: $100.000 (retiro);
- de `jeffdev1` a Relia: $20.000 (retiro).
