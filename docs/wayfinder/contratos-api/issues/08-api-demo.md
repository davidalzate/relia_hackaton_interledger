# API Relia: endpoints del Modo demo

Type: grilling
Status: open
Blocked by: 04, 05, 06
Labels: wayfinder:grilling

## Question

¿Qué expone el tag `demo` de `relia-api.openapi.yaml` (panel E1 y teléfono C2), sabiendo que la mayoría de los pagos son reales?

- Simulador Bre-B: "simular pago recibido" sobre la carga en curso.
- Bandeja del teléfono simulado: mensajes por llave (OTP, invitación, solicitud de pago con `approvalUrl`, avisos, comprobantes) y contador de no leídos.
- Con pagos reales, ¿qué queda de "Mi billetera" y de "aprobar o rechazar carga por Wallet"? ¿Desaparecen a favor de la `approvalUrl` real o se mantiene un atajo simulado?
- Forzar fallos ("el próximo pago falla", "la solicitud vence ya", "usuario rechaza") sobre pagos reales: ¿los aplica el backend sin llamar al riel o se apoya en ganchos del proxy?
- `POST /demo/reset` y el flag de entorno que apaga el tag.

Produce: la sección demo en `contracts/relia-api.openapi.yaml`.
