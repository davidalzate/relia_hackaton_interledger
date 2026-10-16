# Ensamblaje y lint final de los dos contratos

Type: task
Status: claimed
Blocked by:
Labels: wayfinder:task

## Question

Los dos YAML ya existen y Redocly los valida con avisos cosméticos. Falta dejarlos listos para que front, backend y proxy se construyan en paralelo. AFK; el agente lo hace solo:

1. Agregar `info.license` (o desactivar la regla) y decidir los `servers` (local y demo) en [contracts/relia-api.openapi.yaml](../../../../contracts/relia-api.openapi.yaml) y [contracts/rail-proxy.openapi.yaml](../../../../contracts/rail-proxy.openapi.yaml).
2. Agregar `contracts/redocly.yaml` con las reglas y las excepciones acordadas, para que `npx @redocly/cli lint contracts/*.yaml` quede en 0 errores y 0 avisos.
3. Revisar la coherencia entre los dos contratos: conversión de pesos a `{ value, assetCode, assetScale }`, mapeo de estados (pago de riel → operación y liquidación), códigos de error que se propagan (`rail_unavailable`, `relia_grant_unavailable`) e idempotencia.
4. Recorrer el guion de demo de [prototype.md §12](../../../prototype.md) paso a paso contra los endpoints y anotar cualquier hueco. Un hueco que necesite una decisión se convierte en ticket nuevo; no se decide aquí.

Resuelto cuando el lint quede limpio y el recorrido del guion no deje huecos sin ticket.
