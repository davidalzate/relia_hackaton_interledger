# 03: Cliente HTTP del Rail Proxy

Type: task
Status: open
Blocked by: 02
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** implementación real de `IRailClient` que habla con el proxy según `contracts/rail-proxy.openapi.yaml`. Con ella el backend funciona contra el proxy Node sin conocer Open Payments ([ADR 0001](../../../adr/0001-proxy-como-contrato-de-riel.md)).

- [ ] Operaciones: resolver un pointer (`GET /wallet-addresses`), crear un pago (`POST /payments`), consultar un pago (`GET /payments/{id}`) y cancelar un pago (`POST /payments/{id}/cancel`).
- [ ] Toda llamada lleva `X-Api-Key` desde la configuración, junto con la URL base del proxy.
- [ ] Convierte COP enteros a `{ value: pesos × 100, assetCode: "COP", assetScale: 2 }` y de vuelta.
- [ ] Manda como `Idempotency-Key` el id del pago en el backend (`pagos.id`). Trata un 200 por idempotencia igual que un 201.
- [ ] Traduce los errores del proxy a errores de dominio:
  - `rail_unavailable` y `relia_grant_unavailable` → no disponible (la API responde `503 rail_unavailable`);
  - `wallet_not_found` / 404 → billetera inexistente;
  - `payment_not_cancellable` → no cancelable;
  - `unsupported_asset`, `same_wallet` e `idempotency_conflict` → error de dominio explícito.
- [ ] Ante timeouts o errores de red, devuelve "no disponible", nunca una excepción sin manejar.
- [ ] Pruebas contra un servidor HTTP falso que responde según el contrato del proxy: conversión de montos, headers, cada traducción de error y deserialización de `Payment` con campos `null`.
