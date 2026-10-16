# El proxy Node expone un contrato de riel, no un espejo de Open Payments

El backend .NET habla con el proxy Node mediante operaciones de negocio agnósticas al riel: enviar un pago a un payment pointer, solicitar un pago a un payment pointer, consultar y cancelar. No expone los primitivos de Open Payments (grants GNAP, quotes, incoming/outgoing payments). Así el backend nunca conoce GNAP ni guarda llaves de billetera, y un futuro adaptador Bre-B implementa el mismo contrato OpenAPI. Esa es la tesis de defensibilidad del negocio: cambiar de riel es cambiar un adaptador. El costo es que el proxy guarda estado propio (grants pendientes, continuaciones) y tiene su propia máquina de estados del pago de riel, separada de la máquina de estados de la operación que vive en el backend.

## Considered Options

- Espejo casi 1:1 de Open Payments con la orquestación en el backend. Se descartó porque ata el backend al riel y vacía el sentido del adaptador.
