# API Relia: endpoints del Modo demo

Type: grilling
Status: resolved
Blocked by: 04, 05, 06
Labels: wayfinder:grilling

## Question

¿Qué expone el tag `demo` de `relia-api.openapi.yaml` (panel E1 y teléfono C2), sabiendo que la mayoría de los pagos son reales?

- Simulador Bre-B: "simular pago recibido" sobre la carga en curso.
- Bandeja del teléfono simulado: mensajes por llave (OTP, invitación, solicitud de pago con `approvalUrl`, avisos, comprobantes) y contador de no leídos.
- Con pagos reales, ¿qué queda de "Mi billetera" y de "aprobar o rechazar carga por Wallet"? ¿Desaparecen a favor de la `approvalUrl` real o se mantiene un atajo simulado?
- Forzar fallos ("el próximo pago falla", "la solicitud vence ya", "usuario rechaza") sobre pagos reales: ¿los aplica el backend sin llamar al riel o se apoya en ganchos del proxy?
- `POST /demo/reset` y el flag de entorno que apaga el tag.

Ya decidido en el [Contrato de riel: recursos, estados y webhook del proxy](06-contrato-de-riel.md):
- El proxy no tiene ganchos de demo. "El próximo pago falla" lo marca el backend sin llamar al proxy. "La solicitud vence ya" es un `cancel` más el estado `expired` en el backend. "Usuario rechaza" es un rechazo real en la billetera de prueba.
- El usuario aprueba o rechaza en su billetera real de prueba (`approval.url`). Falta decidir qué queda de "Mi billetera" en el teléfono simulado: ¿solo el enlace a la aprobación?
- La siembra del backend debe usar las billeteras reales: Rosa `$ilp.interledger-test.dev/rosa-test` y el usuario demo `$ilp.interledger-test.dev/jeffdev` vinculado al 3000000001. Los ejemplos del esquema (`tienda-rosa`, `usuario-demo`) no existen.

Produce: la sección demo en `contracts/relia-api.openapi.yaml`.

## Answer

Resuelto en grilling (1 ronda, 2026-10-16). Asset: [contracts/relia-api.openapi.yaml](../../../../contracts/relia-api.openapi.yaml), tag `demo` (Redocly: válido).

- **Acceso**: las rutas `/demo/*` van sin login y solo existen con `DEMO_MODE=true`; sin el flag responden 404.
- **Bre-B**: `POST /demo/quota-top-ups/{operationId}/breb-payment` completa la carga pendiente.
- **Teléfono simulado**: una **bandeja global** `GET /demo/messages?phone=`. Cada mensaje dice a qué celular llegó y la respuesta trae `unreadCount`. `POST /demo/messages/read` marca como leídos. Los tipos de mensaje son los del esquema.
- **"Mi billetera"**: `GET /demo/payment-requests?phone=` lista las solicitudes pendientes con `approvalUrl`, y la pantalla muestra "Abrir mi billetera para aprobar". **Aprobar y Rechazar salen de Relia**: ocurren en la wallet de prueba real, también para la carga por Wallet de Rosa.
- **Fallos forzados**:
  - `GET` y `PUT /demo/faults { failNextPayment }`: un solo interruptor, que se apaga solo después de usarse.
  - "La solicitud vence ya" es una acción: `POST /demo/operations/{operationId}/expire`.
  - Se quita el interruptor "Usuario rechaza"; quien presenta rechaza en la wallet de prueba.
- **Reinicio**: `POST /demo/reset` cancela en el riel los cobros pendientes, vacía los datos de Relia y vuelve a sembrar (300 000 0001 → `jeffdev`, 300 000 0002 sin llave, Rosa en ceros). El dinero real ya movido no se devuelve.
- **Corresponsales de ejemplo** sin billetera real: `walletSimulated: true`, y su liquidación se marca `paid` sin usar el riel.
