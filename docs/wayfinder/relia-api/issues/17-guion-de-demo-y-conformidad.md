# 17: Guion de demo de punta a punta y conformidad con el contrato

Type: task
Status: resolved
Blocked by: 08, 10, 16
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** prueba de que el backend corre el guion completo de FLUJOS.md §12 y de que cada respuesta cumple `contracts/relia-api.openapi.yaml`.

- [ ] Una prueba de integración recorre los 8 pasos del guion, de `POST /demo/reset` al pago de comisión, con el doble del riel aprobando los pagos.
- [ ] Las respuestas de las pruebas de integración se validan contra los esquemas del contrato (estado, `content-type` y cuerpo). Si hay diferencias, se corrige el código o se abre un cambio al contrato.
- [ ] Cada `code` del enum `ErrorCode` del contrato está cubierto por al menos una prueba.
- [ ] Checklist manual (opcional, no bloquea el cierre) contra el proxy Node real en la red de prueba:
  - el permiso de Relia está aprobado (FLUJOS §0);
  - el guion se corre con la colección de Postman;
  - el resultado queda anotado en este ticket.
