# 02: Resolver billeteras (`GET /wallet-addresses`)

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El backend confirma que un payment pointer existe y obtiene su URL, su moneda y su escala, como lo necesita la vinculación de llave. El resolvedor convierte el pointer en URL (`$host/ruta` → `https://host/ruta`; sin ruta → `/.well-known/pay`), consulta la wallet address, guarda el resultado en caché unos minutos y separa "no existe" de "el riel no responde". Los pasos de pago reutilizan este mismo resolvedor.

**Blocked by:** 01 (Esqueleto del proxy)

**Status:** ready-for-agent

- [ ] `GET /wallet-addresses?pointer=$ilp.interledger-test.dev/rosa-test` → `200` con `pointer`, `url`, `assetCode: COP` y `assetScale: 2`.
- [ ] Un pointer bien formado pero inexistente → `404 wallet_not_found`.
- [ ] Un pointer mal formado o ausente → `422 validation_error`.
- [ ] Un error de red o un 5xx del servidor de billeteras → `503 rail_unavailable`.
- [ ] El resolvedor deja disponible el `id` canónico para comparar billeteras sin depender del texto del pointer.
