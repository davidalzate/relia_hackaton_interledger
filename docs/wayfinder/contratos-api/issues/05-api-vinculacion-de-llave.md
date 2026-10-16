# API Relia: vinculación pública de llave con OTP

Type: grilling
Status: open
Blocked by:
Labels: wayfinder:grilling

## Question

¿Cómo es el flujo público y sin login de "Vincular billetera" (pantalla C1) en `relia-api.openapi.yaml`?

- Iniciar la vinculación con celular y payment pointer, generar el OTP y enviarlo por SMS simulado. ¿Se valida aquí que el payment pointer exista en el riel (pasando por el proxy)?
- Verificar el OTP: intentos, vencimiento y la respuesta "reemplazaremos la billetera anterior" cuando ya había un pointer.
- Forma del recurso vinculación y qué expone (pointer enmascarado).
- Protección mínima contra abuso de un endpoint público en el demo (rate limit declarado o nada).
- Cómo vuelve la app del corresponsal a ver la llave validada (¿basta con repetir `GET /keys/{phone}`?).

Produce: la sección `keyLinks` en `contracts/relia-api.openapi.yaml`.
