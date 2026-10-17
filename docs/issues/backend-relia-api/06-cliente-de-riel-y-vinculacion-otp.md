# 06: Cliente de riel y vinculación pública con OTP

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** sin iniciar sesión, el usuario escribe su celular y su payment pointer, recibe un código por SMS simulado y, al verificarlo, queda vinculado de forma permanente. Este ticket construye `IRailClient`, el cliente HTTP tipado del proxy de riel, con su primer uso: resolver pointers. También deja lista la base que amplía el depósito:

- `X-Api-Key` en cada llamada;
- la conversión de COP a `{ value, assetCode: "COP", assetScale: 2 }`;
- la traducción de los errores del proxy a errores de dominio;
- timeouts y fallas de red tratados como "no disponible".

**Blocked by:** 05 (Llaves, invitación por WhatsApp y bandeja simulada)

**Status:** ready-for-human (implementado y probado con `curl` contra el proxy real; falta correr la colección de Postman)

- [x] `IRailClient` resuelve pointers contra `GET /wallet-addresses` del proxy, con la URL base y la API key de la configuración.
- [x] Los errores del proxy se traducen así: `wallet_not_found` → billetera inexistente; `rail_unavailable`, timeout o error de red → no disponible. Nunca queda una excepción sin manejar.
- [x] `POST /key-links` valida en este orden:
  - celular con llave → `409 key_already_linked`;
  - celular o billetera de un corresponsal o de Relia → `409 phone_reserved`;
  - billetera ya vinculada → `409 wallet_already_linked`, sin revelar a qué celular;
  - pointer inexistente en el riel → `422 wallet_not_found`;
  - riel caído → `503 rail_unavailable`.
- [x] Responde `201` con `challengeId`, `expiresAt` (+5 min) y `maxAttempts` (5). Deja un SMS `otp` con 6 dígitos. El código se guarda como hash en `otp_challenges`.
- [x] Llamarlo otra vez para el mismo celular invalida el desafío anterior.
- [x] `POST /key-links/{challengeId}/verify` responde:
  - código correcto → `200` con `walletAddress` completo y `linkedAt`;
  - código incorrecto → `422 invalid_otp` con `attemptsRemaining`;
  - plazo vencido → `otp_expired`;
  - sin intentos → `otp_attempts_exceeded`;
  - desafío inexistente → `404`;
  - otro flujo vinculó el celular o la billetera mientras tanto → el mismo `409` que al iniciar.
- [x] El rate limit en memoria responde `429 rate_limited` con `Retry-After`.
- [x] Después de verificar, `GET /keys/{phone}` responde `200`.
- [ ] Verificado con la carpeta `key-links` contra el proxy real: un pointer que existe, uno que no existe, y el proxy apagado para el 503.

## Notas de implementación

- **`IRailClient`** (`Rail/RailClient.cs`):
  - es un cliente tipado con `AddHttpClient`. La URL base, `X-Api-Key` y el timeout (`RailProxy:TimeoutSeconds`, 10 s por defecto) salen de la configuración;
  - toda falla sale como `RailException`: `RailWalletNotFoundException` (`wallet_not_found`) o `RailUnavailableException`;
  - se tratan como no disponible: `rail_unavailable`, `relia_grant_unavailable`, timeout, error de red, cuerpo ilegible, `401` (API key mal configurada, con log de error), `429` y `5xx`;
  - los demás códigos (`unsupported_asset`, `same_wallet`, `idempotency_conflict`…) quedan como `RailException` con su `code`, para que el depósito decida. Al resolver un pointer, cualquiera de ellos (por ejemplo, `validation_error` del proxy) responde `422 wallet_not_found`, y solo la indisponibilidad responde `503`;
  - `RailAmount.FromCop` / `ToCop` convierten COP ↔ `{ value, "COP", 2 }`. `ToCop` rechaza otra moneda o fracciones de peso. Todavía no se usa; lo estrena el depósito.
- **Validación** (`KeyLinkService.EnsureCanLinkAsync`): la usan el inicio y la verificación. Las billeteras se comparan en forma de payment pointer, en minúsculas y sin `/` final de los dos lados (lo escrito y lo guardado), así que `$…/JEFFDEV` y `$…/maria/` chocan con `$…/jeffdev` y `$…/maria`. Esa comparación es más estricta que el índice único de la base, que distingue mayúsculas y `/`. La llave se guarda tal como la escribió el usuario, porque es el eco del contrato. La de Relia se toma de `Relia:WalletAddress` y acepta también la forma `https://`. Reservados: celular o billetera de cualquier corresponsal, y la billetera de Relia.
- **"Reenviar código"**: no se toca el desafío anterior. `verify` solo acepta el más reciente del celular; uno más viejo responde `otp_expired`.
- **OTP**: `RandomNumberGenerator`, 6 dígitos. Se guarda el SHA-256 en hex de `{challengeId sin guiones}:{código}` y se compara en tiempo constante.
- **Intentos**: el desafío se lee con `FOR UPDATE`. Un código incorrecto suma el intento y la transacción se confirma antes de responder `422` (el error se devuelve en lugar de lanzarse dentro de la transacción). El orden en `verify` es:
  1. inexistente → `404`;
  2. sin intentos → `otp_attempts_exceeded`;
  3. vencido o reemplazado → `otp_expired`;
  4. código incorrecto → `invalid_otp`;
  5. conflicto de llave → `409`;
  6. crear la llave.
- **Carrera al insertar la llave**: si otra verificación insertó primero, la violación de `ux_wallet_keys_mobile` / `ux_wallet_keys_wallet` se traduce al mismo `409`. Otra violación única no se traduce. Este caso no se probó: requiere dos verificaciones simultáneas.
- **Rate limit**: es el `RateLimiter` de ASP.NET, una ventana fija de 1 minuto por IP sobre `/key-links` (inicio y verificación). El tope está en `Relia:KeyLinkRateLimitPerMinute` (20). En Azure, detrás de un proxy inverso, todos llegarían con la misma IP mientras no se configure `ForwardedHeaders`. Al rechazar responde `problem+json` `rate_limited` con `Retry-After`.
- **Ojo al levantar el proxy**: el `dist/` de `rail-proxy` estaba desactualizado (no tenía `/wallet-addresses` y respondía `404 not_found`). Hay que correr `npm run build` antes de `npm start`, o usar `npm run dev`.
- **Verificado con `curl`** contra el proxy real (`tsx src/main.ts` con su `.env`):
  - `3000000001` → `key_already_linked`;
  - celular de un corresponsal, billetera de Rosa o de Relia → `phone_reserved`;
  - `$…/JEFFDEV` → `wallet_already_linked`;
  - `noexiste-xyz-123` → `422 wallet_not_found`;
  - pointer mal formado → `422 validation_error`;
  - `maria` → `201` y SMS `otp`; reenviar → el desafío viejo con su código correcto da `otp_expired`;
  - código incorrecto → `attemptsRemaining` 4; correcto → `200`, y luego `GET /keys` → `200` (`$ilp...test.dev/ma***`); verificar otra vez → `409 key_already_linked`;
  - 5 fallos → `attemptsRemaining` 0 y después `otp_attempts_exceeded`;
  - dos celulares con la misma billetera: el segundo en verificar → `wallet_already_linked`;
  - desafío envejecido por SQL → `otp_expired`;
  - en la base, `code_hash` es hex de 64 caracteres y `attempts` quedó guardado;
  - después de la revisión: `$…/maria/` y `$…/MARIA` → `wallet_already_linked`, y `$…/rosa-test/` → `phone_reserved`;
  - proxy apagado → `503` sin crear el desafío;
  - proxy en una IP sin ruta y timeout de 2 s → `503` a los 2 s;
  - tope de 3 → la cuarta petición da `429` con `Retry-After: 60`, y `verify` también queda limitado.

  En la base local quedaron llaves de prueba para `3009993661` (`maria`) y `3009993664` (`cliente`), y desafíos de `30099936xx`. `3000000002` sigue sin llave.
