# 03: Permiso permanente de Relia (`/relia-grant`)

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El operador pide el permiso permanente de la billetera de Relia con un tope y un intervalo ISO 8601 repetitivo, lo aprueba una vez en la wallet de prueba y el proxy lo detecta solo. El proxy pide un grant interactivo `outgoing-payment` sobre la billetera de Relia, sin `receiver` y sin `finish`, y guarda la continuación. En este ticket nace el **sincronizador de fondo**: un ciclo que toma su trabajo del almacén y consulta continuaciones respetando `continue.wait`. Los tickets de pagos lo reutilizan.

**Blocked by:** 01 (Esqueleto del proxy)

**Status:** ready-for-agent

- [ ] `POST /relia-grant` con `debitLimit` COP/2 e `interval` válido → `202` con `status: awaiting_approval` y `approvalUrl`.
- [ ] Un `debitLimit` en otra moneda o escala → `422 unsupported_asset`. Un `interval` inválido → `422 validation_error`. Si el riel no responde → `503 rail_unavailable`.
- [ ] Al aprobar en la wallet de prueba, `GET /relia-grant` pasa a `active` con `debitLimit`, `interval` y `approvedAt`, sin ninguna acción adicional. `/health` pasa a `ok`.
- [ ] Una respuesta `too_fast` solo reprograma la consulta. Un rechazo del operador devuelve el estado anterior.
- [ ] Mientras hay una petición sin aprobar, el estado es `awaiting_approval` aunque exista un permiso activo anterior.
- [ ] Un permiso nuevo aprobado reemplaza al anterior, y el anterior se revoca en el riel sin bloquear si eso falla.
- [ ] Al reiniciar el proxy, el permiso sigue `active`, y una petición pendiente se sigue consultando.
- [ ] Tokens, `manage` y continuaciones solo están en el almacén: nunca en respuestas ni en logs.
