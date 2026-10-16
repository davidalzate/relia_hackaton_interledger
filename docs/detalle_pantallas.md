Relia: detalle de pantallas
Inventario completo de pantallas, modales y estados del prototipo Relia.dc.html. Para cada una: propósito, cómo se llega, estructura de arriba hacia abajo, textos, validaciones, estados y acciones.
Convenciones visuales comunes:
Fondo de página gris claro #F5F5F5, tarjetas blancas con radio 16 a 24 px.
Botón principal: píldora cobalto #2E3192, texto blanco, alto 52 px. Deshabilitado: gris #E8E8E8 con texto #707070.
Botón secundario: píldora blanca con borde gris; al pasar el cursor el borde se vuelve cobalto.
Insignia Simulación: píldora amarilla clara #FDF1C4, texto #5C4700, borde punteado.
Insignias de estado: Pendiente (ámbar), Completado (verde), Fallido (rojo suave), Vencido y Cancelado (gris).
Pantallas de éxito: fondo verde #1E7A36, texto blanco.
Montos con números tabulares; los montos son el elemento más grande de cada vista.
Enlace de regreso "← Caja" arriba a la izquierda en las vistas internas del corresponsal.
Ancho máximo de contenido: 560 px en vistas operativas, 1240 px en historial y admin.

Índice
A. Acceso
A1. Login
B. App del corresponsal
B0. Encabezado y menú
B1. Caja (inicio)
B2. Depositar
B3. Retirar
B4. Modal de confirmación de efectivo
B5. Modal "Esta llave no existe" (invitación WhatsApp)
B6. Cargar cupo
B7. Efectivo en caja
B8. Historial
B9. Comprobante
C. Usuario final (simulado)
C1. Vincular billetera (página pública)
C2. Teléfono del usuario
D. Admin Relia
D0. Encabezado admin
D1. Inicio admin
D2. Detalle de corresponsal
D3. Modal de pago de comisión
E. Herramientas de demo
E1. Modo demo

A1. Login
Propósito: entrada única para corresponsal y admin. Se llega: al abrir el prototipo y al cerrar sesión.
Estructura (tarjeta centrada, ancho máx. 400 px):
Wordmark "relia." (Goldplay Alt, 40 px, cobalto con punto coral).
Subtítulo: "Tu caja de efectivo y dinero digital."
Campo Correo (precargado rosa@demo.relia).
Campo Contraseña (precargado demo1234).
Mensaje de error (solo si aplica).
Botón principal Entrar.
Ayuda: "Corresponsal: rosa@demo.relia / demo1234" y "Admin Relia: admin@relia.co / admin1234".
Enlace "¿Eres usuario? Vincula tu celular" → C1.
Validación y destino:
rosa@demo.relia + demo1234 → B1 Caja.
admin@relia.co + admin1234 → D1 Inicio admin.
Otro valor → "Correo o contraseña incorrectos." (fondo rojo suave).
Teclado: Enter envía el formulario.

B0. Encabezado y menú (corresponsal)
Presente en: Caja, Depositar, Retirar, Cargar cupo, Efectivo en caja, Historial. Estructura: barra blanca fija arriba.
Izquierda: wordmark "relia." + "Tienda Doña Rosa". Al tocarlo → Caja.
Derecha: botón de menú (ícono de tres líneas, 44 × 44 px).
Menú desplegable:
Rosa Gómez / rosa@demo.relia (informativo).
Historial → B8.
Efectivo en caja → B7.
Cerrar sesión (texto rojo) → A1; limpia cualquier flujo en curso.

B1. Caja (inicio)
Propósito: ver saldos de un vistazo y lanzar operaciones. Se llega: tras el login, desde el wordmark o "← Caja".
Estructura:
Aviso cupo bajo (si cupo < $50.000): banner ámbar "Cupo bajo, carga cupo para seguir depositando."
Aviso efectivo bajo (si efectivo < $50.000): banner ámbar "Efectivo bajo, asigna más para pagar retiros." + enlace "Asignar efectivo".
Tarjeta cobalto:
"Cupo disponible" + monto grande (38 px, blanco).
Separador.
"Comisión acumulada" + monto en amarillo.
Tarjeta Efectivo en caja: etiqueta + monto + botón secundario "Asignar efectivo" → B7.
Acciones (cuadrícula 2 × 2):
Depositar (coral, flecha ↓) → B2.
Retirar (cobalto, flecha ↑) → B3.
+ Cargar cupo (blanco) → B6.
Historial (blanco) → B8.
Últimas operaciones: título + enlace "Ver todo" → B8.
Hasta 3 filas: tipo, hora · identificador, valor, insignia de estado si no está completada.
Vacío: "Aún no hay operaciones. Empieza asignando efectivo y cargando cupo."

B2. Depositar
Propósito: el usuario entrega efectivo y recibe dinero en su billetera. Se llega: acción "Depositar" en Caja o "Nueva operación" tras un depósito.
B2.1 Formulario (una sola vista)
"← Caja".
Título Depositar + subtítulo "El usuario entrega efectivo y recibe dinero en su billetera."
Tarjeta blanca con:
Celular del usuario: campo numérico, formato 300 000 0001, máx. 10 dígitos. Cambiarlo borra la validación previa.
Mensajes de llave (ver B2.2).
Monto: campo grande con prefijo $, separador de miles automático.
Casilla "Que el usuario reciba el valor redondo" con la ayuda "La comisión de $1.000 se cobra aparte."
Error de monto (si 0 < M ≤ 1.000): "El monto debe ser mayor a $1.000."
Desglose (si M > 1.000):
Bloque azul claro "Dile al usuario":
Destacado: "Entrega en efectivo" + monto grande.
Recibe en su billetera.
Comisión Relia $1.000.
Bloque gris "Para ti":
Recibes en efectivo.
Tu comisión +$500.
Cupo después.
Efectivo en caja después.
Error de cupo (si el cupo no alcanza): "Cupo insuficiente, faltan $X" + enlace "Cargar cupo".
Botón principal:
Antes de validar: Validar llave (activo solo con celular de 10 dígitos, monto válido y cupo suficiente).
Llave validada: Confirmar depósito (deshabilitado si el monto o el cupo no cumplen).
Casilla "Simular que la llave no existe" + insignia Simulación (solo visible antes de validar).
B2.2 Resultado de la validación
Llave encontrada: chip verde "Llave encontrada" + pointer oculto $ilp...test.dev/us***. Cualquier celular de 10 dígitos es válido.
Celular incompleto: "Escribe un celular de 10 dígitos."
Llave no existe (casilla activa): abre B5 automáticamente y muestra un recuadro ámbar "Este celular no tiene billetera vinculada" + botón verde "Invitar por WhatsApp". Tras enviar: "Invitación enviada. Cuando el usuario vincule su billetera, valida de nuevo." + "Abrir teléfono del usuario".
B2.3 Confirmación
"Confirmar depósito" abre B4 con: "¿Confirmas que recibiste en efectivo un total de $X por parte del usuario?"
B2.4 Enviando
Tarjeta centrada: indicador giratorio, "Enviando a la billetera", monto que recibirá la billetera, "No cierres esta pantalla." Dura 2 s.
B2.5 Completado
Tarjeta verde:
"Depósito completado".
Monto enviado a la billetera (grande) + "llegaron a la billetera del usuario".
Resumen: efectivo recibido, comisión Relia, cupo antes, cupo después, efectivo en caja antes → después, operación.
Botones: Nueva operación (blanco), Ver comprobante (borde) → B9, "Volver a la caja" (enlace).
Efecto: el usuario recibe un SMS simulado "Recibiste $X en tu billetera…".
B2.6 Error
Tarjeta blanca con borde rojo:
Título y texto según el caso:
Error de red: "El simulador no respondió. No se movió dinero. Intenta de nuevo."
Cupo insuficiente: "No hay cupo para este depósito. Carga cupo y vuelve a intentar."
"Operación RL-XXXX · $M".
Botones: Intentar de nuevo (vuelve al formulario con los datos) y "Volver a la caja".

B3. Retirar
Propósito: el usuario paga desde su billetera y recibe efectivo. Se llega: acción "Retirar" en Caja.
B3.1 Formulario
Igual que B2.1, con estas diferencias:
Subtítulo: "El usuario paga desde su billetera y recibe efectivo."
Desglose:
Dile al usuario: destacado "Recibe en efectivo" + monto; paga desde su billetera; comisión Relia $1.000.
Para ti: entregas en efectivo; tu comisión +$500; cupo después; efectivo en caja después.
Error de efectivo: "Efectivo insuficiente, faltan $X" + enlace "Asignar efectivo" → B7.
Botón final tras validar: Enviar solicitud de pago.
B3.2 Esperando aprobación
Tarjeta centrada:
Indicador giratorio.
"Esperando que el usuario apruebe en su billetera".
Monto que paga el usuario (grande).
Contador regresivo desde 5:00 en píldora ámbar.
Aviso rojo destacado: "No entregues efectivo todavía".
"Abrir teléfono del usuario" + insignia Simulación → C2.
"Cancelar solicitud" (texto rojo).
Efecto: llega un SMS al usuario y aparece la solicitud en "Mi billetera".
B3.3 Completado (entrega)
Tarjeta verde:
"Pago aprobado".
"Entrega en efectivo" + monto muy grande (52 px).
Resumen: pagó desde su billetera, comisión Relia, cupo antes/después, efectivo en caja antes → después, operación.
Botón Listo → abre B4 con "¿Confirmas que entregaste en efectivo un total de $X al usuario?"; al confirmar → Caja.
Ver comprobante → B9.
B3.4 Error
Mismo formato que B2.6. Casos:
El usuario rechazó el pago: "No se movió dinero. No entregues efectivo."
La solicitud venció: "Se acabó el tiempo. No se movió dinero."
Solicitud cancelada: "Cancelaste la solicitud. No se movió dinero."
El pago falló: "La billetera no completó el pago. No se movió dinero."
Efectivo insuficiente (al aprobar): "No tienes efectivo suficiente para este retiro. Asigna más efectivo."

B4. Modal de confirmación de efectivo
Propósito: confirmación explícita del manejo de billetes. Estructura: fondo cobalto translúcido; tarjeta centrada (máx. 420 px).
Etiqueta "Confirma el efectivo".
Pregunta (20 px):
Depósito: "¿Confirmas que recibiste en efectivo un total de $X por parte del usuario?"
Retiro: "¿Confirmas que entregaste en efectivo un total de $X al usuario?"
Sí, confirmo (principal).
No, revisar (secundario): cierra sin hacer nada.

B5. Modal "Esta llave no existe"
Propósito: ofrecer invitar al usuario a vincular su billetera. Se abre: al validar con "Simular que la llave no existe" activa, o con "Invitar por WhatsApp".
Estructura:
Título "Esta llave no existe" + insignia Simulación.
"El 300 000 0002 no tiene billetera vinculada. ¿Quieres enviarle una invitación por WhatsApp?"
"Enlace generado": https://wa.me/57XXXXXXXXXX?text=Hola%2C%20te%20invito…
"Mensaje": burbuja verde con "Hola, te invito a vincular tu wallet en relia.com/selectkey".
Sí, enviar mensaje (verde): cierra, marca como enviada y deja el mensaje en C2 con el botón "Abrir enlace para vincular".
Ahora no: cierra.

B6. Cargar cupo
Propósito: subir el saldo operativo del corresponsal. Se llega: acción "Cargar cupo" en Caja o enlace desde el error de cupo.
Encabezado: "← Caja", título Cargar cupo, "Cupo actual $X".
B6.1 Paso 1: método
Etiqueta "Paso 1 · Elige el método". Dos tarjetas seleccionables:
Bre-B: "Genera un QR y paga desde cualquier banco. Hasta 10 minutos." + Simulación.
Wallet: "Envía una solicitud a la billetera de tu llave y apruébala allí." + Simulación.
B6.2 Paso 2: datos
Etiqueta "Paso 2 · Bre-B" o "Paso 2 · Wallet" + enlace "Cambiar método".
Monto a cargar (prefijo $, miles automáticos).
Solo Wallet: Tu llave (celular), precargada 310 555 0101, con la ayuda "Enviaremos la solicitud a la billetera vinculada a esta llave."
Botón: Generar QR (Bre-B) o Enviar solicitud (Wallet). Activo con monto > 0 y, en Wallet, llave de 10 dígitos.
B6.3 Esperando pago
Insignia "Simulación Bre-B" o "Simulación Wallet".
Bre-B: QR de ejemplo sobre fondo azul claro.
Wallet: "Aprueba la solicitud en tu billetera" + "Llave 310 555 0101".
Monto grande.
Píldora ámbar con punto pulsante: "Esperando pago · m:ss" (10:00 Bre-B, 5:00 Wallet).
"Confirma el pago desde el panel Modo demo."
Cancelar (secundario).
B6.4 Completado
Tarjeta verde: "Cupo cargado", "+$X", cupo antes y cupo después, operación, botón Volver a la caja.
B6.5 Error
Tarjeta con borde rojo. Casos: Pago rechazado ("La carga no se completó. Tu cupo no cambió."), La solicitud venció, Solicitud cancelada. Botones Intentar de nuevo (vuelve al paso 2) y "Volver a la caja".

B7. Efectivo en caja
Propósito: definir el efectivo físico disponible para pagar retiros. Se llega: tarjeta de efectivo en Caja, menú, aviso de efectivo bajo o error de efectivo insuficiente.
Estructura:
"← Caja", título Efectivo en caja, ayuda "Define cuánto efectivo tienes para pagar retiros. Los depósitos lo suben y los retiros lo bajan."
Tarjeta cobalto: "Efectivo disponible" + monto.
Tarjeta blanca:
Selector segmentado: Sumar efectivo | Fijar total.
Campo de monto ("Efectivo que agregas" o "Total de efectivo en caja").
Fila "Efectivo después" con la vista previa.
Guardar efectivo (activo con monto > 0 al sumar, o cualquier monto al fijar).
Tras guardar: recuadro verde "Efectivo actualizado · $antes → $después · RL-XXXX" + "Volver a la caja".
Registro: fila "Asignación de efectivo" (sumar) o "Ajuste de efectivo" (fijar) en el historial; cupo y comisión no cambian.

B8. Historial
Propósito: auditoría completa de saldos. Se llega: acción Historial, "Ver todo" o el menú.
Estructura:
"← Caja", título Historial, resumen "Cupo $X · Comisión $X · Efectivo $X".
Ayuda: "Toca una operación para ver su comprobante."
Filtros en píldoras (desplazables): Todas, Depósito, Retiro, Carga de cupo, Efectivo, Pago de comisión. La activa va en cobalto.
Contenido:
Escritorio (≥ 900 px): tabla con columnas Fecha, Tipo, Valor, Cupo antes, Cupo después, Comisión antes, Comisión después, Efectivo antes, Efectivo después, Estado.
Móvil: tarjeta por operación con tipo, fecha, valor, insignia de estado y una cuadrícula 2 × 3 con los seis saldos antes/después.
Operaciones pendientes muestran "Pendiente" en los saldos.
Vacío: "No hay operaciones de este tipo. Cuando hagas una, aparecerá aquí."
Tocar una fila o tarjeta → B9.
Orden: más reciente primero.

B9. Comprobante
Propósito: soporte de la operación para corresponsal, usuario o admin. Se abre: "Ver comprobante" en pantallas de éxito o tocando una operación en B8 o D2.
Estructura (modal, máx. 400 px):
Tarjeta blanca:
Wordmark "relia." + insignia de estado (color según estado).
"Comprobante de {tipo}" + monto principal grande + leyenda:
Depósito: monto enviado, "enviados a la billetera del usuario".
Retiro: efectivo, "entregados en efectivo".
Carga: "cargados a tu cupo".
Efectivo: "agregados a la caja" o "nuevo total en caja".
Pago de comisión: "pagados en comisiones".
Separador punteado.
Filas etiqueta / valor:
Siempre: Fecha, Operación.
Depósito/retiro: Celular (300 *** 0001), Billetera (oculta), efectivo entregado/recibido, enviado/pagado desde la billetera, Comisión Relia, Modalidad ("Comisión incluida" o "Valor redondo (comisión aparte)").
Carga: Método, Cupo antes, Cupo después.
Efectivo: Efectivo antes, Efectivo después.
Pago de comisión: Destino, Comisión antes, Comisión después.
Siempre: Estado; Motivo si falló.
Pie: "{Corresponsal} · Corresponsal Relia. Guarda este recibo como soporte de tu operación."
Botones bajo la tarjeta:
SMS · Simulación (solo depósito y retiro): envía el comprobante al teléfono del usuario; cambia a "SMS enviado".
Imprimir.
Cerrar (principal).

C1. Vincular billetera (página pública)
Propósito: que el usuario asocie su celular (llave) a su payment pointer. Se llega: "Inscribir celular (página pública)" en Modo demo, enlace del login o "Abrir enlace para vincular" en el WhatsApp (celular precargado).
Marco: fondo azul hielo, sin encabezado del corresponsal. Arriba: wordmark + insignia "Página del usuario · Simulación".
C1.1 Paso 1 de 3
Título "Vincula tu billetera a tu celular".
Texto: "Así podrás depositar y retirar efectivo en las tiendas Relia solo con tu número."
Celular (10 dígitos).
Payment pointer (ejemplo $ilp.interledger-test.dev/maria).
Errores: "Escribe un celular de 10 dígitos." / "El payment pointer empieza por $ y tiene una ruta…"
Enviar código → genera un OTP de 6 dígitos y lo envía por SMS a C2.
C1.2 Paso 2 de 3
Título "Escribe el código".
"Te enviamos un SMS al 300 000 0002." + Simulación.
Si el celular ya tenía otro pointer: recuadro ámbar "Reemplazaremos la billetera anterior." + pointer anterior.
Campo de 6 dígitos grande, espaciado, centrado.
Error: "El código no coincide. Revisa el SMS."
Verificar.
Secundarios: Ver SMS (abre C2) y Cambiar datos (vuelve al paso 1).
C1.3 Paso 3 de 3
Check verde.
"Tu celular quedó vinculado a tu billetera".
"{celular} ahora es tu llave. Ya puedes depositar y retirar en cualquier tienda Relia."
Pointer vinculado.
Volver a la app del corresponsal: regresa a la pantalla anterior; si es el mismo celular del depósito o retiro en curso, la llave queda validada.
Enlace inferior permanente (pasos 1 y 2): "Volver a la app del corresponsal".

C2. Teléfono del usuario
Propósito: simular el celular del usuario final. Se abre: Modo demo, "Abrir teléfono del usuario" en la espera de retiro o en B2.2, y "Ver SMS" en C1.
Marco: teléfono negro (360 × 720 px máx.) con pantalla blanca redondeada, sobre fondo oscuro translúcido.
Barra: "Teléfono del usuario" + Simulación.
Pestañas: Mensajes | Mi billetera.
C2.1 Mensajes
Lista de SMS y WhatsApp, el más reciente primero: canal · remitente, hora, texto.
WhatsApp de invitación: botón verde Abrir enlace para vincular → C1.
Vacío: "Sin mensajes por ahora. Aquí llegan los SMS y WhatsApp de la demo."
Tipos de mensaje: OTP, invitación, solicitud de pago, depósito recibido, retiro pagado, vinculación exitosa, comprobante, pago de comisión.
C2.2 Mi billetera
Tarjeta cobalto: "Mi billetera" + $ilp.interledger-test.dev/usuario-demo.
Si hay solicitud pendiente: tarjeta con borde amarillo, "Solicitud de pago · m:ss", "Tienda Doña Rosa te pide", monto, Aprobar (principal) y Rechazar (secundario, texto rojo).
Vacío: "No tienes solicitudes pendientes."
Botón inferior: Cerrar teléfono. El contador de no leídos se reinicia al abrirlo.

D0. Encabezado admin
Barra cobalto fija: wordmark blanco + insignia amarilla "Admin" (al tocarlo → D1). Derecha: Cerrar sesión → A1.

D1. Inicio admin
Propósito: visión global de la red y gestión de comisiones.
Estructura (ancho máx. 1240 px):
Título Corresponsales.
Indicadores (cuadrícula adaptable):
Total dinero movido (tarjeta cobalto): suma de depósitos y retiros completados + número de operaciones.
Depósitos: suma + cantidad.
Retiros: suma + cantidad.
Comisiones por pagar (coral).
Comisiones pagadas hoy (verde).
Tabla de corresponsales (desplazamiento horizontal en móvil):
Corresponsal (nombre como enlace → D2, responsable debajo).
Ciudad, Operaciones, Depósitos, Retiros, Total movido, Cupo, Efectivo.
Comisión por pagar (coral, negrita).
Acción: Pagar comisión → D3, o "Sin comisión".
Pagos de comisión: lista con corresponsal, hora · identificador PG-XXX · pointer, monto. Vacío: "Aún no has pagado comisiones hoy."
Tienda Doña Rosa refleja en vivo lo que se opera en la app del corresponsal.

D2. Detalle de corresponsal
Se llega: enlace del nombre en la tabla de D1.
Estructura:
"← Corresponsales".
Nombre (título) + "Responsable · Ciudad · payment pointer".
Resumen: Cupo, Efectivo, Comisión por pagar + botón Pagar comisión (o "Sin comisión").
Ayuda "Toca una operación para ver su comprobante."
Tabla del historial con las mismas columnas que B8 (fecha, tipo, valor, cupo, comisión y efectivo antes/después, estado). Fila → B9.
Vacío: "Este corresponsal aún no tiene operaciones." (sin tabla).

D3. Modal de pago de comisión
Estructura: etiqueta "Pago de comisión" + Simulación.
Confirmar: monto grande, "Para {corresponsal}", pointer destino, "La comisión acumulada del corresponsal quedará en $0.", Confirmar pago y Cancelar.
Enviando: indicador giratorio + "Enviando $X a {corresponsal}" (~2 s).
Completado: tarjeta verde "Comisión pagada", monto, "{corresponsal} · {id}", botón Listo.
Error (interruptor de fallo): "El pago falló. No se movió dinero.", Intentar de nuevo y Cerrar.
Efectos: comisión del corresponsal en $0, fila "Pago de comisión" en su historial, registro en "Pagos de comisión" y, para Rosa, SMS de aviso.

E1. Modo demo
Acceso: botón flotante amarillo abajo a la derecha, visible en todas las pantallas, con contador de mensajes nuevos.
Panel (cobalto oscuro, máx. 340 px): título "Modo demo" + Simulación.
Simulador Bre-B: Simular pago recibido (solo con QR en curso; si no, "Sin QR en curso.").
Billetera del corresponsal: Aprobar / Rechazar (solo con carga por Wallet en curso; si no, "Sin solicitud de carga en curso.").
Usuario: Inscribir celular (página pública) → C1; Abrir teléfono del usuario → C2 (con contador).
Forzar fallos (interruptores):
El próximo pago falla (se apaga solo tras usarse).
La solicitud vence ya (se apaga solo tras usarse).
Usuario rechaza (permanece activo).
Reiniciar demo: datos en ceros, borra mensajes, flujos y pagos; vuelve a Caja o a Inicio admin según la sesión.

Mapa de navegación
Login ─┬─ Corresponsal → Caja ─┬─ Depositar ─→ Confirmación → Enviando → Completado → Comprobante
       │                       ├─ Retirar ───→ Esperando ─→ Entrega → Confirmación → Caja
       │                       ├─ Cargar cupo → Método → Datos → Esperando → Completado
       │                       ├─ Efectivo en caja
       │                       └─ Historial → Comprobante
       ├─ Admin → Inicio admin ─┬─ Detalle corresponsal → Comprobante
       │                        └─ Pago de comisión
       └─ Vincular billetera (público) → OTP → Vinculado

Modo demo (global) → Simuladores · Teléfono del usuario · Vincular · Fallos · Reiniciar

