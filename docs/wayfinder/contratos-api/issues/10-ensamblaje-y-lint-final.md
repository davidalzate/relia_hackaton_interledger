# Ensamblaje y lint final de los dos contratos

Type: task
Status: resolved
Blocked by:
Labels: wayfinder:task

## Question

Los dos YAML ya existen y Redocly los valida con avisos cosméticos. Falta dejarlos listos para que front, backend y proxy se construyan en paralelo. AFK; el agente lo hace solo:

1. Agregar `info.license` (o desactivar la regla) y decidir los `servers` (local y demo) en [contracts/relia-api.openapi.yaml](../../../../contracts/relia-api.openapi.yaml) y [contracts/rail-proxy.openapi.yaml](../../../../contracts/rail-proxy.openapi.yaml).
2. Agregar `contracts/redocly.yaml` con las reglas y las excepciones acordadas, para que `npx @redocly/cli lint contracts/*.yaml` quede en 0 errores y 0 avisos.
3. Revisar la coherencia entre los dos contratos: conversión de pesos a `{ value, assetCode, assetScale }`, mapeo de estados (pago de riel → operación y liquidación), códigos de error que se propagan (`rail_unavailable`, `relia_grant_unavailable`) e idempotencia.
4. Recorrer el guion de demo de [prototype.md §12](../../../prototype.md) paso a paso contra los endpoints y anotar cualquier hueco. Un hueco que necesite una decisión se convierte en ticket nuevo; no se decide aquí.

Resuelto cuando el lint quede limpio y el recorrido del guion no deje huecos sin ticket.

## Answer

Hecho (AFK, 2026-10-16).

**Lint**: [contracts/redocly.yaml](../../../../contracts/redocly.yaml). `npx @redocly/cli@1.25.11 lint --config contracts/redocly.yaml` da 0 errores y 0 avisos en ambos contratos. Se apagaron `info-license` (repo sin licencia definida) y `no-server-example.com` (los servidores son de desarrollo local). Se agregó un `429` a `/health` del proxy.

**Coherencia entre contratos** (correcciones aplicadas):
- Crear depósito, retiro y carga, cancelar y "vencer ya" no declaraban el `503 rail_unavailable` que puede venir del proxy (riel caído o Relia sin permiso activo). Ahora lo declaran, con el mismo criterio que la liquidación: no se crea nada.
- `Idempotency-Key`: tras un 4xx o 503 no se creó nada y se puede reintentar con la misma clave; tras una operación fallida se usa una clave nueva.
- La tabla de traducción de estados del pago de riel a operación y liquidación quedó en `FailureCode`.
- Dinero: pesos × 100 → `{ value, "COP", 2 }`, ya documentado en el proxy. Idempotencia hacia el proxy = `pagos.id`, consistente.

**Recorrido del guion de §12**: los pasos 1–5, 7 y 8 se cubren con los endpoints existentes. Huecos:
- Paso 6: no hay billetera libre para vincular el 300 000 0002 → [Billetera real para el celular sin llave del guion](11-billetera-para-el-celular-sin-llave.md) (task).
- La vinculación no valida la moneda → [¿Se rechaza al vincular una billetera que no es COP?](12-moneda-al-vincular-llave.md) (grilling).
- Requisito operativo, no del contrato: antes de presentar, el operador debe aprobar el permiso permanente de Relia (`POST /relia-grant` del proxy). Sin eso, los depósitos y la liquidación de Rosa responden 503.
