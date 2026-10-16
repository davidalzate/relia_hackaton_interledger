# ¿Se rechaza al vincular una billetera que no es COP?

Type: grilling
Status: open
Blocked by:
Labels: wayfinder:grilling

## Question

La vinculación pública ([API Relia: vinculación pública de llave con OTP](05-api-vinculacion-de-llave.md)) valida que el payment pointer exista, pero dejó la moneda para después. El riel ya devuelve `assetCode` y `assetScale` en `GET /wallet-addresses`, y las billeteras del demo son COP con escala 2.

Si alguien vincula una billetera en USD o EUR, el primer depósito fallaría en el proxy con `422 unsupported_asset`, ya con el efectivo recibido. ¿Se rechaza al iniciar la vinculación con `422 wallet_currency_unsupported`? ¿O se acepta y la conversión queda a cargo del riel (quotes multimoneda)?

Produce: el ajuste en `POST /key-links` y en `ErrorCode` de `contracts/relia-api.openapi.yaml`.
