# 06: Cliente de riel y vinculación pública con OTP

**Parent:** [Backend .NET de Relia](../backend-relia-api.md)

**What to build:** sin iniciar sesión, el usuario escribe su celular y su payment pointer, recibe un código por SMS simulado y, al verificarlo, queda vinculado de forma permanente. Este ticket construye `IRailClient`, el cliente HTTP tipado del proxy de riel, con su primer uso: resolver pointers. También deja lista la base que amplía el depósito:

- `X-Api-Key` en cada llamada;
- la conversión de COP a `{ value, assetCode: "COP", assetScale: 2 }`;
- la traducción de los errores del proxy a errores de dominio;
- timeouts y fallas de red tratados como "no disponible".

**Blocked by:** 05 (Llaves, invitación por WhatsApp y bandeja simulada)

**Status:** ready-for-agent

- [ ] `IRailClient` resuelve pointers contra `GET /wallet-addresses` del proxy, con la URL base y la API key de la configuración.
- [ ] Los errores del proxy se traducen así: `wallet_not_found` → billetera inexistente; `rail_unavailable`, timeout o error de red → no disponible. Nunca queda una excepción sin manejar.
- [ ] `POST /key-links` valida en este orden:
  - celular con llave → `409 key_already_linked`;
  - celular o billetera de un corresponsal o de Relia → `409 phone_reserved`;
  - billetera ya vinculada → `409 wallet_already_linked`, sin revelar a qué celular;
  - pointer inexistente en el riel → `422 wallet_not_found`;
  - riel caído → `503 rail_unavailable`.
- [ ] Responde `201` con `challengeId`, `expiresAt` (+5 min) y `maxAttempts` (5). Deja un SMS `otp` con 6 dígitos. El código se guarda como hash en `otp_challenges`.
- [ ] Llamarlo otra vez para el mismo celular invalida el desafío anterior.
- [ ] `POST /key-links/{challengeId}/verify` responde:
  - código correcto → `200` con `walletAddress` completo y `linkedAt`;
  - código incorrecto → `422 invalid_otp` con `attemptsRemaining`;
  - plazo vencido → `otp_expired`;
  - sin intentos → `otp_attempts_exceeded`;
  - desafío inexistente → `404`;
  - otro flujo vinculó el celular o la billetera mientras tanto → el mismo `409` que al iniciar.
- [ ] El rate limit en memoria responde `429 rate_limited` con `Retry-After`.
- [ ] Después de verificar, `GET /keys/{phone}` responde `200`.
- [ ] Verificado con la carpeta `key-links` contra el proxy real: un pointer que existe, uno que no existe, y el proxy apagado para el 503.
