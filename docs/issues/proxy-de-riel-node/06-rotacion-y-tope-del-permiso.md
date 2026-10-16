# 06: Vida del permiso: rotación de token y tope agotado

**Parent:** [Proxy de riel en Node](../proxy-de-riel-node.md)

**What to build:** El permiso de Relia sigue sirviendo horas después de aprobado, y el operador sabe cuándo se agotó el tope. El gestor de grants rota el token (`token.rotate`) en single-flight cuando faltan menos de ~30 s para `expires_in` o tras un `401` del servidor de recursos, y siempre guarda el `value` y el `manage` más recientes. Los grants no interactivos (`incoming-payment`, `quote`) quedan en caché por servidor de autorización y se piden de nuevo al vencer.

**Blocked by:** 04 (Relia paga)

**Status:** ready-for-agent

- [ ] Un `outgoing` creado después de que venció el primer token funciona sin intervención del operador.
- [ ] Dos pagos simultáneos que necesitan rotar disparan una sola rotación.
- [ ] El permiso sigue `active` después de un reinicio con un token vencido: se rota al primer uso.
- [ ] Si el riel rechaza un outgoing por exceder el tope → `503 relia_grant_unavailable` y `GET /relia-grant` → `exhausted`. Vuelve a `active` al empezar el siguiente periodo del `interval`.
- [ ] Si al rotar el riel dice que el grant fue revocado → el permiso pasa a `missing` y `/health` a `degraded`.
