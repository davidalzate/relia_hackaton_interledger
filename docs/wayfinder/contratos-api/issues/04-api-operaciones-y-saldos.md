# API Relia: recursos de operaciones, saldos e historial del corresponsal

Type: grilling
Status: open
Blocked by:
Labels: wayfinder:grilling

## Question

¿Cómo se modelan en `relia-api.openapi.yaml` las operaciones del corresponsal y sus saldos? Hay que decidir:

- Un recurso único `/operations` con `type` (`deposit | withdrawal | quotaTopUp | cashAdjustment | commissionPayout`) o endpoints por tipo para crear y uno unificado para leer.
- Si el backend expone un endpoint de **cotización o vista previa** (desglose "Dile al usuario" / "Para ti", cupo y efectivo después, errores de cupo o efectivo) o si el frontend calcula el desglose con las reglas de §5.
- La validación de llave antes de confirmar (`GET /keys/{phone}` con el pointer enmascarado) y cómo se integra la invitación por WhatsApp.
- La forma del recurso operación: estado, motivo, montos (efectivo, billetera, comisión, modalidad), saldos antes y después (`quota`, `accruedCommission`, `cashOnHand`), `approvalUrl` y vencimiento cuando aplique.
- Cancelación (`POST /operations/{id}/cancel`) y confirmación del efectivo entregado en el retiro (¿es un evento del API o solo de la UI?).
- La carga de cupo: Bre-B (QR simulado) y Wallet (llave del corresponsal → su pointer).
- Login y saldos de la caja (`GET /agents/me`), historial con filtros y paginación, comprobante (¿recurso propio o la misma operación?) y "enviar comprobante por SMS".

Produce: la sección del corresponsal en `contracts/relia-api.openapi.yaml`.
