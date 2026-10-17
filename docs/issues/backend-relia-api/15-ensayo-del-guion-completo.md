# 15: Ensayo del guion completo contra la red de prueba (HITL)

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** antes de presentar, una persona corre de punta a punta el guion de FLUJOS.md §12 con la colección de Postman de la Relia API. El ensayo usa el backend, la base en Docker recién creada y el proxy de riel real en la red de prueba. Anota en este ticket lo que observa. HITL: hace falta aprobar y rechazar en la wallet de prueba.

**Blocked by:** 08 (Recibos de depósito y retiro), 14 (Modo demo: fallos forzados, "vencer ya" y reinicio)

**Status:** ready-for-agent

- [ ] Arranque en limpio: borrar el volumen, `docker compose up`, arrancar el backend con `DEMO_MODE=true` e iniciar sesión como Rosa.
- [ ] El permiso permanente de Relia está aprobado en el proxy (FLUJOS §0).
- [ ] Los 8 pasos del guion pasan, de `POST /demo/reset` al pago de comisión de Rosa, o cada falla queda anotada con su ticket.
- [ ] Cada fila de la tabla de traducción de estados se observó al menos una vez: aprobar, rechazar, dejar vencer, cancelar y `failNextPayment`.
- [ ] Cada `code` de error del contrato se vio al menos una vez o queda anotado como no alcanzable a mano.
- [ ] Las respuestas coinciden con `relia-api.openapi.yaml` (estado, `content-type` y forma del cuerpo). Las diferencias se corrigen en el código o abren un cambio al contrato.
- [ ] `v_agent_balance_check` da los tres descuadres en 0 al final del guion.
- [ ] Queda anotado si el paso 6 se pudo correr: necesita una billetera COP libre (ticket 11 de contratos-api).
