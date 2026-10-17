# 09: Ensayo completo contra la red de prueba (HITL)

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** Antes de presentar, una persona corre el proxy contra la red de prueba real con la colección de Postman y las billeteras `relia-test1`, `rosa-test` y `jeffdev`. Recorre el checklist de 15 puntos de la sección Testing Decisions de la spec y anota en la spec lo que observa. HITL: hace falta aprobar y rechazar en la wallet de prueba.

**Blocked by:** 05 (Idempotencia), 06 (Vida del permiso), 08 (Vencimiento y cancelación)

**Status:** ready-for-agent

- [ ] Los 15 puntos del checklist pasan, o cada falla queda anotada con su ticket.
- [ ] Quedan anotados los valores reales: `expires_in` del token, `continue.wait`, la vida de la quote y el código y mensaje del riel al exceder el tope.
- [ ] Queda confirmado si la red de prueba exige interacción para `quote` o `incoming-payment`.
- [ ] Revisión de logs: no aparecen tokens, `manage`, continuaciones, la llave ni la API key.
- [ ] El permiso de Relia queda aprobado y la billetera de Relia con fondos para el demo.
