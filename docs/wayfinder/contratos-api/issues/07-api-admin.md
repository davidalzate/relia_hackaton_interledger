# API Relia: panel admin y pago de comisión

Type: grilling
Status: open
Blocked by: 04
Labels: wayfinder:grilling

## Question

¿Qué endpoints con rol `admin` necesita el panel (pantallas D1–D3) en `relia-api.openapi.yaml`?

- Indicadores: total movido, depósitos, retiros, comisiones por pagar y pagadas hoy. ¿Un endpoint agregado o se calculan en el frontend?
- Lista de corresponsales con métricas, detalle con historial (¿reutiliza la representación de operación del ticket de operaciones?) y lista de pagos de comisión.
- Pago de comisión como operación `commissionPayout` sobre el riel real: creación, estados, idempotencia y qué pasa si la comisión cambia mientras el pago está en curso.

Produce: la sección admin en `contracts/relia-api.openapi.yaml`.
