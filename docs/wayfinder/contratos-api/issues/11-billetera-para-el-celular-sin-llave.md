# Billetera real para el celular sin llave del guion

Type: task
Status: open
Blocked by:
Labels: wayfinder:task

## Question

El paso 6 del guion ([prototype.md §12](../../../prototype.md)) vincula en vivo el 300 000 0002 a una billetera. Con las reglas del contrato **ninguna de las tres billeteras existentes sirve**:
- `relia-test1` y `rosa-test` son de Relia y de un corresponsal → `409 phone_reserved`.
- `jeffdev` ya está vinculada al 300 000 0001 → `409 wallet_already_linked`.

HITL, checklist:

1. En `wallet.interledger-test.dev`, crear una cuarta billetera en COP para el "usuario nuevo" del guion. Primero probar si la cuenta de `jeffdev` permite una segunda wallet address en la misma cuenta COP. Si no, registrar otro usuario con otro correo.
2. Confirmar con `curl https://ilp.interledger-test.dev/<nombre>` que es COP con `assetScale: 2`.
3. Anotar aquí el payment pointer. Lo escribe quien presenta en la página pública de vinculación; no va en la siembra.

Resuelto cuando el pointer quede anotado.
