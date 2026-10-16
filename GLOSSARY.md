# Relia

Relia convierte comercios de barrio en puntos de efectivo digital: orquesta el cupo, las comisiones y la conciliación del corresponsal sobre cualquier riel de pagos. Cada término lleva entre paréntesis su identificador canónico en inglés.

## Actores

**Corresponsal** (`agent`):
Comercio con una cuenta virtual en Relia y una caja única, donde el usuario entrega o recibe efectivo.
_Avoid_: comercio, tienda, merchant, punto

**Usuario** (`customer`):
Persona final que convierte efectivo en dinero digital o al revés, identificada por su llave.
_Avoid_: cliente, user, titular

**Admin Relia** (`admin`):
Operador de Relia que ve a todos los corresponsales y les paga comisiones.
_Avoid_: superusuario, backoffice

## Identidad del usuario

**Llave** (`key`):
Celular de 10 dígitos que se resuelve a exactamente un payment pointer: el del usuario vinculado o el del corresponsal registrado.
_Avoid_: alias, teléfono, número

**Payment pointer** (`walletAddress`):
Dirección de una billetera en el riel (en Open Payments, la wallet address).
_Avoid_: wallet, cuenta, dirección ILP

**Vinculación** (`keyLink`):
Asociación permanente de una llave a un payment pointer, confirmada con OTP; no se reemplaza ni se borra.
_Avoid_: registro, inscripción, enrolamiento

## Saldos del corresponsal

**Cupo** (`quota`):
Saldo operativo del corresponsal en su cuenta virtual Relia; baja con depósitos, sube con retiros y cargas.
_Avoid_: saldo, balance, crédito

**Efectivo en caja** (`cashOnHand`):
Efectivo que Relia espera en la caja del corresponsal según sus operaciones y ajustes; no es un conteo físico.
_Avoid_: caja, efectivo disponible, cash

**Comisión acumulada** (`accruedCommission`):
Lo que Relia le debe al corresponsal; sube $500 por operación completada y vuelve a $0 con la liquidación.
_Avoid_: ganancia, saldo de comisión

**Reserva** (`reserved`):
Cupo o efectivo comprometido por operaciones pendientes, que no se puede usar en otra operación ni bajar con un ajuste.
_Avoid_: bloqueo, retención

**Movimiento** (`movement`):
Fila del libro del corresponsal que registra cupo, comisión y efectivo antes, la variación y después.
_Avoid_: transacción, asiento

**Historial** (`history`):
Lista de entradas del corresponsal: operaciones en cualquier estado, ajustes de efectivo y liquidaciones.
_Avoid_: extracto, log

## Operaciones

**Operación** (`operation`):
Depósito, retiro o carga de cupo; sus montos quedan fijos al crearla y su estado es final cuando sale de pendiente.
_Avoid_: transacción, movimiento

**Depósito** (`deposit`):
El usuario entrega efectivo y Relia paga a su billetera.
_Avoid_: cash-in, recarga, consignación

**Retiro** (`withdrawal`):
El usuario paga desde su billetera a Relia y recibe efectivo del corresponsal.
_Avoid_: cash-out, retirada

**Carga de cupo** (`quotaTopUp`):
Aumento del cupo pagado por el corresponsal, por Bre-B o por Wallet.
_Avoid_: recarga, fondeo

**Ajuste de efectivo** (`cashAdjustment`):
Cambio manual del efectivo en caja, sumando un monto o fijando un total.
_Avoid_: asignación, arqueo

**Liquidación** (`settlement`):
Pago de toda la comisión acumulada de un corresponsal a su billetera; en pantalla se llama "Pago de comisión".
_Avoid_: dispersión, commission payout

**Pago** (`payment`):
Intento de mover dinero por un riel para una operación o una liquidación; una operación puede tener varios.
_Avoid_: transferencia, cobro

**Monto solicitado** (`requestedAmount`, M):
Valor que escribe el corresponsal al iniciar una operación; en depósito y retiro debe ser mayor a $1.000.
_Avoid_: valor, importe

**Monto neto** (`netAmount`):
Lo que recibe el usuario: dinero en su billetera (depósito) o efectivo (retiro).
_Avoid_: monto recibido

**Monto cobrado** (`chargedAmount`):
Lo que paga el usuario: el monto neto más la comisión Relia.
_Avoid_: total, monto pagado

**Recibo** (`receipt`):
Contenido congelado de un depósito o retiro completado, con código y huella; desde la segunda emisión sale como copia.
_Avoid_: comprobante, voucher, factura

**Valor redondo** (`roundAmount`):
Modalidad en la que la comisión de $1.000 se cobra aparte y el usuario recibe exactamente el monto.
_Avoid_: comisión aparte, monto neto

**Comisión Relia** (`fee`):
Los $1.000 que paga el usuario por operación, repartidos $500 para Relia y $500 para el corresponsal.
_Avoid_: tarifa, cargo

## Riel

**Riel** (`rail`):
Red de pagos sobre la que se mueve el dinero (Open Payments en el demo, Bre-B en el negocio real).
_Avoid_: red, pasarela, proveedor

**Adaptador de riel** (`railAdapter`):
Componente que traduce las operaciones de Relia a un riel concreto; cambiar de riel es cambiar de adaptador.
_Avoid_: conector, integración, proxy
