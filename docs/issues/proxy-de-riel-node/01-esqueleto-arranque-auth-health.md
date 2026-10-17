# 01: Esqueleto del proxy: arranque, auth y `/health`

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El proyecto `rail-proxy/` (Node 22, TypeScript, Fastify, `@interledger/open-payments`) arranca con solo variables de entorno: puerto (4000 por defecto), API key, payment pointer de Relia, key ID, llave privada en base64 y ruta del archivo SQLite. Al arrancar resuelve la billetera de Relia y crea el almacén. Si falta algo o la billetera no se resuelve, se niega a arrancar con un mensaje claro. `GET /health` es público y responde con datos en memoria. Todas las demás rutas exigen `X-Api-Key`. Cualquier error sale en `application/problem+json` con un `code` del enum del contrato, y una falla de esquema es `422 validation_error`, no el 400 de Fastify.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] Sin una variable obligatoria, o con un pointer de Relia que no resuelve, el proceso termina con un error que nombra la causa.
- [ ] `GET /health` sin key responde `200` con `status: degraded`, `rail: openPayments`, `reliaGrant: missing` y `reliaWallet` (pointer, url, COP, 2).
- [ ] `/health` responde `429` en `problem+json` al pasar el límite por IP.
- [ ] Una ruta protegida sin key, o con key incorrecta, responde `401 unauthorized` (comparación en tiempo constante).
- [ ] Los errores llevan `type: about:blank`, `title`, `status` y `code`.
- [ ] Los logs no muestran la llave privada ni la API key.
- [ ] El README del proxy explica cómo configurarlo y correrlo.
