# 08: Vinculación pública de llave con OTP

Type: task
Status: open
Blocked by: 03, 07
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** sin iniciar sesión, el usuario escribe su celular y su payment pointer, recibe un código por SMS simulado y, al verificarlo, queda vinculado de forma permanente (C1, flujo 8).

- [ ] `POST /key-links` valida antes de enviar el código, en este orden:
  - el celular ya tiene llave → `409 key_already_linked`;
  - el celular o la billetera son de un corresponsal o de Relia → `409 phone_reserved`;
  - la billetera ya está vinculada a otro celular → `409 wallet_already_linked`, sin revelar cuál;
  - el pointer no existe en el riel (vía `IRailClient`) → `422 wallet_not_found`;
  - el riel no responde → `503 rail_unavailable`.
- [ ] Responde 201 con `challengeId`, `expiresAt` (+5 min) y `maxAttempts` (5). Deja un SMS `otp` con un código de 6 dígitos.
- [ ] Llamarlo otra vez para el mismo celular invalida el desafío anterior.
- [ ] `POST /key-links/{challengeId}/verify` responde:
  - código correcto → 200 con `walletAddress` completo y `linkedAt`;
  - código incorrecto → `422 invalid_otp` con `attemptsRemaining`;
  - plazo vencido → `otp_expired`;
  - sin intentos → `otp_attempts_exceeded`;
  - desafío inexistente → 404;
  - si otro flujo vinculó el celular o la billetera mientras tanto → el mismo 409 que al iniciar.
- [ ] Rate limit con `429 rate_limited` y `Retry-After`.
- [ ] Después de verificar, `GET /keys/{phone}` responde 200.
- [ ] Hay pruebas HTTP con el reloj falso para el vencimiento y con el doble del riel para `wallet_not_found` y 503.
