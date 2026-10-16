# 13: Barrido de fondo de pagos pendientes

Type: task
Status: open
Blocked by: 11
Labels: ready-for-agent
Parent: [01 Implementar backend Relia API](01-implementar-backend-relia-api.md)

**What to build:** las operaciones (y después las liquidaciones) pendientes avanzan aunque nadie consulte su estado: el backend refresca sus pagos y vence las solicitudes pasadas de plazo.

- [ ] Hay un servicio de fondo que cada 15 s consulta en el proxy los pagos de las operaciones pendientes y aplica la misma lógica que la lectura.
- [ ] Vence las operaciones pasadas de `expiresAt` y cancela en el riel.
- [ ] Si el proxy no responde, la operación sigue `pending` y se reintenta en el siguiente ciclo.
- [ ] Deja un punto de extensión para que el ticket de liquidaciones registre su seguimiento.
- [ ] Las pruebas disparan el barrido de forma determinista (sin esperar 15 s reales) y verifican el resultado por HTTP.
