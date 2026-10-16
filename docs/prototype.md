Relia: especificación final del prototipo
Documento de referencia con las reglas de negocio, pantallas, estados y decisiones tomadas en el prototipo Relia.dc.html. Todo el estado vive en memoria; no hay backend. Lo simulado lleva la insignia "Simulación".

1. Qué es Relia
App para corresponsales (comercios de barrio) que convierte efectivo en dinero digital (depósito) y dinero digital en efectivo (retiro). El usuario final usa su celular como llave, vinculado a una billetera Interledger (payment pointer). Relia también tiene un panel de administración para ver a todos los corresponsales y pagarles comisiones.
2. Actores y accesos
Actor
Acceso
Credenciales demo
Corresponsal
Login → Caja
rosa@demo.relia / demo1234
Admin Relia
Login → Panel admin
admin@relia.co / admin1234
Usuario final
Página pública "Vincular billetera" y teléfono simulado
Sin login


El login decide el destino según las credenciales. Credenciales incorrectas muestran "Correo o contraseña incorrectos."
3. Conceptos del corresponsal
Cupo: saldo operativo en su cuenta virtual Relia. Baja con depósitos, sube con retiros y con cargas.
Efectivo en caja: billetes físicos disponibles. Sube con depósitos, baja con retiros, se ajusta manualmente.
Comisión acumulada: lo que Relia le debe al corresponsal. Sube $500 por operación completada; vuelve a $0 cuando el admin la paga.
No hay apertura ni cierre de turno. Caja única.
4. Moneda y formatos
Solo COP. Formato $50.000: signo $, punto de miles, sin decimales.
Números tabulares en todos los montos.
Celular con formato 300 000 0001 (10 dígitos).
Payment pointer oculto parcialmente: $ilp...test.dev/us***.
Celular oculto en comprobantes: 300 *** 0001.
Ningún texto usa el carácter raya larga.
5. Reglas de cálculo
Sea M el monto que escribe el corresponsal. Debe ser mayor a 1.000**.Comisiónalusuario:**1.000 por operación. De eso, $500 se acumulan para el corresponsal (los otros $500 son de Relia).
Depósito (el usuario entrega efectivo)
Modalidad
Usuario entrega en efectivo
Recibe en billetera
Cupo
Efectivo en caja
Comisión incluida (casilla apagada)
M
M − 1.000
baja M
sube M
Valor redondo (casilla encendida)
M + 1.000
M
baja M + 1.000
sube M + 1.000


Se bloquea si el cupo es menor a lo que debe bajar ("Cupo insuficiente, faltan $X").
Retiro (el usuario paga desde su billetera)
Modalidad
Usuario paga desde billetera
Recibe en efectivo
Cupo
Efectivo en caja
Comisión incluida
M
M − 1.000
sube M − 1.000
baja M − 1.000
Valor redondo
M + 1.000
M
sube M
baja M


Se bloquea si el efectivo en caja es menor a lo que se debe entregar ("Efectivo insuficiente, faltan $X"). Se vuelve a verificar al momento de la aprobación.
Carga de cupo
Cupo sube el monto cargado. Comisión y efectivo no cambian.
Efectivo en caja
Sumar efectivo: efectivo sube el monto.
Fijar total: efectivo queda en el monto escrito (la diferencia se registra).
Cupo y comisión no cambian.
Pago de comisión (admin)
Comisión del corresponsal pasa de X a $0. Cupo y efectivo no cambian.
Regla general
Cupo, comisión y efectivo solo cambian cuando la operación pasa a completado, nunca antes.
6. Estados de una operación
pendiente → completado | fallido | vencido | cancelado
Operación
Cómo se completa
Tiempo límite
Depósito
Automático tras 2 s
No aplica
Retiro
El usuario aprueba en su billetera
5:00
Carga Bre-B
"Simular pago recibido" en Modo demo
10:00
Carga Wallet
Aprobar en Modo demo (billetera del corresponsal)
5:00
Asignar efectivo
Inmediato
No aplica
Pago de comisión
Automático tras ~2 s
No aplica


Cada cambio de estado agrega o actualiza una fila del historial. Las filas fallidas, vencidas o canceladas guardan el motivo y dejan antes = después.
Errores diseñados: error de red, cupo insuficiente, efectivo insuficiente, usuario rechazó, solicitud vencida, solicitud cancelada, pago rechazado.
7. Pantallas del corresponsal
7.1 Login
Correo y contraseña precargados, botón "Entrar". Enlace "¿Eres usuario? Vincula tu celular" lleva a la página pública.
7.2 Caja (inicio)
Encabezado: wordmark "relia.", nombre del comercio, menú (Historial, Efectivo en caja, Cerrar sesión).
Avisos ámbar: cupo bajo (< $50.000) y efectivo bajo (< $50.000).
Tarjeta grande: Cupo disponible y Comisión acumulada.
Tarjeta: Efectivo en caja con botón "Asignar efectivo".
Acciones: Depositar, Retirar, Cargar cupo, Historial.
Últimas 3 operaciones, con estado si no está completada. Estado vacío si no hay operaciones.
7.3 Depositar / Retirar (una sola vista)
Todo en un formulario para agilizar:
Celular del usuario (llave).
Monto.
Casilla "Que el usuario reciba el valor redondo".
Desglose en vivo, separado en dos bloques:
Dile al usuario: efectivo que entrega/recibe (en grande), lo que va o sale de su billetera, comisión Relia.
Para ti: efectivo que recibes/entregas, tu comisión +$500, cupo después, efectivo en caja después.
Botón "Validar llave".
Casilla "Simular que la llave no existe" (insignia Simulación), debajo del botón.
Validación de llave (simulada): todo celular de 10 dígitos se considera válido y muestra el chip "Llave encontrada" con el pointer parcialmente oculto. Si la casilla de simulación está activa, la llave no existe y se pregunta si se quiere invitar por WhatsApp.
Depósito: "Confirmar depósito" abre la confirmación: "¿Confirmas que recibiste en efectivo un total de $X por parte del usuario?" Al confirmar: "Enviando a la billetera" (2 s) y luego pantalla verde "Depósito completado" con resumen, "Nueva operación", "Ver comprobante" y "Volver a la caja".
Retiro: "Enviar solicitud de pago" muestra la espera "Esperando que el usuario apruebe en su billetera" con contador 5:00, el monto, el aviso "No entregues efectivo todavía", "Abrir teléfono del usuario" y "Cancelar solicitud". Al aprobarse: pantalla verde "Entrega en efectivo $X". El botón "Listo" pide confirmar: "¿Confirmas que entregaste en efectivo un total de $X al usuario?" El efectivo solo aparece como entregable en estado completado.
7.4 Invitación por WhatsApp
Modal "Esta llave no existe" con el enlace https://wa.me/57XXXXXXXXXX?text=... y el mensaje:
Hola, te invito a vincular tu wallet en relia.com/selectkey
Botones "Sí, enviar mensaje" y "Ahora no". El mensaje llega a la bandeja del teléfono simulado con un botón para abrir la página de vinculación. Si el usuario vincula ese mismo celular y se vuelve a la app, la llave queda validada.
7.5 Cargar cupo
Paso 1: elegir Bre-B o Wallet (ambas con insignia Simulación).
Paso 2 Bre-B: monto, "Generar QR", QR de ejemplo, monto, "Esperando pago" con contador 10:00.
Paso 2 Wallet: monto y llave (celular) del corresponsal (precargada 310 555 0101), "Enviar solicitud", espera "Aprueba la solicitud en tu billetera" con contador 5:00 y "Cancelar".
Éxito: cupo antes y cupo después. Errores: pago rechazado, vencida, cancelada.
7.6 Efectivo en caja
Efectivo disponible, selector Sumar efectivo / Fijar total, monto, vista previa "Efectivo después", "Guardar efectivo". Confirmación con antes → después e identificador. Cada cambio queda en el historial ("Asignación de efectivo" o "Ajuste de efectivo").
7.7 Historial
Filtros: Todas, Depósito, Retiro, Carga de cupo, Efectivo, Pago de comisión.
Tabla en escritorio (≥ 900 px), tarjetas en móvil.
Columnas: fecha, tipo, valor, cupo antes/después, comisión antes/después, efectivo antes/después, estado.
Al tocar una operación se abre su comprobante.
7.8 Comprobante
Disponible desde las pantallas de éxito y desde cualquier fila del historial (corresponsal y admin). Contenido según tipo:
Depósito/retiro: fecha, operación, celular oculto, billetera oculta, montos de efectivo y billetera, comisión, modalidad, estado.
Carga de cupo: método, cupo antes/después.
Efectivo: efectivo antes/después.
Pago de comisión: destino, comisión antes/después.
Insignia de estado, motivo si falló, nombre del corresponsal en el pie.
Acciones: enviar por SMS (simulado, solo depósito/retiro), imprimir, cerrar.
8. Pantallas del usuario (simuladas)
8.1 Vincular billetera (página pública)
Accesible desde Modo demo, desde el login o desde el enlace del WhatsApp.
Celular y payment pointer, "Enviar código".
Código de 6 dígitos. "Te enviamos un SMS" (Simulación). El código llega a la bandeja del teléfono. Si el celular ya tenía otro pointer: "Reemplazaremos la billetera anterior".
"Tu celular quedó vinculado a tu billetera" y botón para volver a la app del corresponsal.
8.2 Teléfono del usuario
Marco de teléfono con dos pestañas:
Mensajes: SMS y WhatsApp simulados (OTP, invitaciones, avisos de depósito, solicitudes, comprobantes, pago de comisión).
Mi billetera: solicitud de pago pendiente con monto, comercio, contador, "Aprobar" y "Rechazar". Estado vacío si no hay solicitudes.
9. Panel admin Relia
9.1 Inicio
Indicadores: Total dinero movido (depósitos + retiros completados), Depósitos (suma y cantidad), Retiros (suma y cantidad), Comisiones por pagar, Comisiones pagadas hoy.
Tabla de corresponsales: nombre (enlace al detalle) y responsable, ciudad, operaciones, depósitos, retiros, total movido, cupo, efectivo, comisión por pagar, acción "Pagar comisión" (o "Sin comisión").
Lista de pagos de comisión realizados.
Los totales cuentan solo operaciones completadas, usando el valor M.
9.2 Detalle de corresponsal
Nombre, responsable, ciudad, payment pointer, cupo, efectivo, comisión por pagar, botón "Pagar comisión" y tabla completa del historial. Cada fila abre el comprobante.
9.3 Pago de comisión (Simulación)
Confirmación con monto, corresponsal y pointer destino → "Enviando" → "Comisión pagada" con identificador. La comisión queda en $0 y se registra una fila "Pago de comisión" en el historial del corresponsal. Si es Tienda Doña Rosa, le llega un SMS. Puede fallar con el interruptor de fallos.
9.4 Corresponsales de ejemplo
Corresponsal
Responsable
Ciudad
Prefijo
Tienda Doña Rosa (en vivo)
Rosa Gómez
Bogotá
RL
Droguería San José
Luis Pérez
Medellín
SJ
Minimercado El Paisa
Diana Ríos
Cali
MP
Papelería La 70
Jorge Mora
Bogotá
PL


Los historiales de ejemplo se generan aplicando exactamente las reglas de la sección 5.
10. Modo demo (panel de simuladores)
Botón flotante amarillo "Modo demo" con contador de mensajes nuevos.
Simulador Bre-B: "Simular pago recibido".
Billetera del corresponsal: aprobar o rechazar carga por Wallet.
Usuario: "Inscribir celular (página pública)" y "Abrir teléfono del usuario".
Forzar fallos:
"El próximo pago falla": se apaga solo tras usarse. Aplica a depósito (error de red), retiro, cargas y pago de comisión.
"La solicitud vence ya": vence la solicitud pendiente y se apaga solo.
"Usuario rechaza": permanece activo; rechaza cada retiro nuevo tras ~2,5 s.
Reiniciar demo: vuelve al estado inicial.
11. Datos iniciales
Tienda Doña Rosa inicia en ceros: cupo $0, efectivo $0, comisión $0, historial vacío.
Usuario vinculado de ejemplo: 300 000 0001 → $ilp.interledger-test.dev/usuario-demo.
Payment pointer del corresponsal: $ilp.interledger-test.dev/tienda-rosa. Llave: 310 555 0101.
12. Guion de demo sugerido
Entrar como Rosa. Ver avisos de cupo y efectivo bajos.
Asignar efectivo (por ejemplo $300.000).
Cargar cupo por Bre-B y simular el pago.
Depositar $50.000 al 300 000 0001: ver desglose, alternar valor redondo, confirmar efectivo recibido, ver comprobante.
Retirar $100.000: abrir teléfono, aprobar, ver "Entrega en efectivo", confirmar entrega.
Depositar con "Simular que la llave no existe": enviar invitación, abrir enlace en el teléfono, vincular con OTP y volver.
Revisar historial y abrir un comprobante.
Cerrar sesión, entrar como admin, revisar totales, abrir el detalle de Rosa y pagar su comisión.
13. Decisiones de diseño
Identidad visual: Pagui Design System (cobalto #2E3192, coral #E8596A, amarillo #F5C518, hielo #D4E8EC; Goldplay Alt para títulos, Poppins para texto). Se usó en lugar de la paleta verde azulada del brief original.
Wordmark "relia." en minúsculas con punto coral.
Botones principales en cobalto (contraste AA); coral para acentos; verde oscuro #1E7A36 para pantallas de éxito; ámbar para pendientes; rojo suave para errores.
Mobile first, vistas compactas para minimizar el scroll; tablas en escritorio y tarjetas o desplazamiento horizontal en móvil.
Una acción principal por pantalla; confirmación explícita del efectivo en depósitos y retiros.
Todo lo simulado lleva la insignia "Simulación".
Tema oscuro: no implementado.
