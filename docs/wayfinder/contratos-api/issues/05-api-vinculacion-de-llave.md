# API Relia: vinculación pública de llave con OTP

Type: grilling
Status: resolved
Blocked by:
Labels: wayfinder:grilling

## Question

¿Cómo es el flujo público y sin login de "Vincular billetera" (pantalla C1) en `relia-api.openapi.yaml`?

- Iniciar la vinculación con celular y payment pointer, generar el OTP y enviarlo por SMS simulado. ¿Se valida aquí que el payment pointer exista en el riel (pasando por el proxy)?
- Verificar el OTP: intentos, vencimiento y la respuesta "reemplazaremos la billetera anterior" cuando ya había un pointer.
- Forma del recurso vinculación y qué expone (pointer enmascarado).
- Protección mínima contra abuso de un endpoint público en el demo (rate limit declarado o nada).
- Cómo vuelve la app del corresponsal a ver la llave validada (¿basta con repetir `GET /keys/{phone}`?).
- **Conflicto con el esquema** ([relia_schema_hackathon_v0.2.sql](../../../relia_schema_hackathon_v0.2.sql), tabla `llaves_wallet`): las llaves son inmutables y tanto el celular como la wallet son únicos. El modelo de negocio y el prototipo dicen que "vincular de nuevo reemplaza el anterior". ¿Cuál gana? ¿Y qué pasa si la misma wallet se intenta vincular a otro celular?
- El esquema guarda los OTP como hash, con un máximo de 5 intentos y vencimiento (`desafios_otp`).

Produce: la sección `keyLinks` en `contracts/relia-api.openapi.yaml`.

## Answer

Resuelto en grilling (1 ronda, 2026-10-16). Asset: [contracts/relia-api.openapi.yaml](../../../../contracts/relia-api.openapi.yaml), tag `keyLinks` (Redocly: válido). [GLOSSARY.md](../../../../GLOSSARY.md) actualizado: la vinculación es permanente.

- **Gana el esquema: la llave es inmutable.** Si el celular ya tiene llave, responde `409 key_already_linked` antes de enviar el OTP. Desaparece el aviso "Reemplazaremos la billetera anterior" del prototipo y el reemplazo de llave queda fuera de alcance.
- Si la wallet ya está vinculada a otro celular, responde `409 wallet_already_linked` sin revelar a cuál.
- **Llaves de usuario y de corresponsal no se mezclan**: responde `409 phone_reserved` si el celular es de un corresponsal o si la wallet es de un corresponsal o de Relia.
- **El pointer se valida en el riel** al iniciar (`422 wallet_not_found`, `503 rail_unavailable`). Requiere un endpoint de resolución en el contrato de riel.
- **Endpoints**:
  - `POST /key-links { phone, walletAddress }` responde `201 { challengeId, expiresAt, maxAttempts }`. Llamarlo de nuevo reenvía el código e invalida el desafío anterior.
  - `POST /key-links/{challengeId}/verify { code }` responde `200 { phone, walletAddress, linkedAt }`, con el pointer completo como eco de lo que escribió el usuario.
- **OTP**: 6 dígitos, vence en 5 min, 5 intentos. Errores `invalid_otp` (con `attemptsRemaining`), `otp_expired` y `otp_attempts_exceeded`.
- **Abuso**: `429 rate_limited` con `Retry-After`; sin captcha.
- **Volver a la app**: el corresponsal repite `GET /keys/{phone}`. El enlace de invitación lleva `?phone=` para precargar el celular.
