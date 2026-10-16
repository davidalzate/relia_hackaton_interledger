# Prompts de frontend — Óscar Correa

Registro de los prompts usados con **Claude Code** (app de escritorio, modelo Claude Opus) para construir el frontend de Relia en `frontend/relia`. Complementa [AI_USAGE.md](../../AI_USAGE.md).

- **Fecha:** 16 de octubre de 2026, una sola sesión.
- **Stack resultante:** Angular 21 standalone, zone.js, signals, PrimeNG 21 (Aura), Zod (`zod/mini`) y SCSS con tokens en `:root`.
- **Flujo de trabajo:** cada pantalla se pidió pegando su especificación (A1, B0–B9, D0–D1). Claude implementó la pantalla, corrió typecheck, build y tests y la probó en el navegador. Luego, con un segundo prompt, hizo el commit y abrió el PR hacia `main`.
- **Qué hicimos nosotros:** escribimos las especificaciones de cada pantalla, revisamos y mergeamos cada PR y decidimos cuando había alternativas (por ejemplo, quedarnos en Angular 21 en lugar de bajar a 19).

## Resumen

| # | Prompt | Resultado |
|---|--------|-----------|
| 1 | Validar cambios y separar lo que no va al repo | Revisión del scaffold y `.gitignore` |
| 2 | Convenciones visuales del equipo → theme | Tokens y clases en `styles.css` + `AGENTS.md` |
| 3 | Rama `feat/theme` y PR a main | [PR #2](https://github.com/davidalzate/relia_hackaton_interledger/pull/2) |
| 4 | Validar la app contra la infraestructura "Angular solo front" | Informe de brechas (sin cambios) |
| 5 | Alinear a la infraestructura, quedándose en Angular 21 | Cambios locales (entraron con el prompt 6) |
| 6 | Commit y PR de la infraestructura | [PR #3](https://github.com/davidalzate/relia_hackaton_interledger/pull/3) |
| 7 | A1. Login | [PR #4](https://github.com/davidalzate/relia_hackaton_interledger/pull/4) |
| 8 | B0. Encabezado y menú del corresponsal | [PR #5](https://github.com/davidalzate/relia_hackaton_interledger/pull/5) |
| 9 | B1. Caja (inicio) | [PR #6](https://github.com/davidalzate/relia_hackaton_interledger/pull/6) |
| 10 | B2.1. Depositar: formulario | [PR #7](https://github.com/davidalzate/relia_hackaton_interledger/pull/7) |
| 11 | B2.3. Confirmación del depósito | PR #7 (commit adicional) |
| 12 | B2.4–B2.6. Enviando, completado y error | PR #7 (commit adicional) |
| 13 | B3. Retirar | [PR #8](https://github.com/davidalzate/relia_hackaton_interledger/pull/8) |
| 14 | B4. Modal de confirmación de efectivo | [PR #9](https://github.com/davidalzate/relia_hackaton_interledger/pull/9) |
| 15 | B5. Modal "Esta llave no existe" | [PR #10](https://github.com/davidalzate/relia_hackaton_interledger/pull/10) |
| 16 | B6. Cargar cupo | [PR #11](https://github.com/davidalzate/relia_hackaton_interledger/pull/11) |
| 17 | B7. Efectivo en caja | [PR #12](https://github.com/davidalzate/relia_hackaton_interledger/pull/12) |
| 18 | B8. Historial | [PR #13](https://github.com/davidalzate/relia_hackaton_interledger/pull/13) |
| 19 | B9. Comprobante | [PR #14](https://github.com/davidalzate/relia_hackaton_interledger/pull/14) |
| 20 | D1. Inicio admin | [PR #15](https://github.com/davidalzate/relia_hackaton_interledger/pull/15) |
| 21 | D0. Encabezado admin | [PR #16](https://github.com/davidalzate/relia_hackaton_interledger/pull/16) |
| 22 | Este registro de prompts | Este archivo |

Después de cada pantalla venía un prompt corto para hacer el commit y abrir el PR. Esos no se listan uno por uno; su forma era siempre la misma:

> Commitea en feat/&lt;rama&gt; y abre PR a main (incluye los commits de PRs anteriores si siguen abiertos).

## Prompts completos

Las especificaciones se pegaron tal cual en el prompt; se reproducen aquí sin cambios.

### 1. Validar cambios del scaffold

> valida los cambios y agrega solo los necesarios los que no deban ir al repo agegalos al archivo md

### 2. Convenciones visuales → theme

> agreguemos estas convenciones al proyeto y theme, soon las predefinidas por el equipo

<details>
<summary>Convenciones pegadas</summary>

```
Convenciones visuales comunes:
- Fondo de página gris claro `#F5F5F5`, tarjetas blancas con radio 16 a 24 px.
- Botón principal: píldora cobalto `#2E3192`, texto blanco, alto 52 px. Deshabilitado: gris `#E8E8E8` con texto `#707070`.
- Botón secundario: píldora blanca con borde gris; al pasar el cursor el borde se vuelve cobalto.
- Insignia **Simulación**: píldora amarilla clara `#FDF1C4`, texto `#5C4700`, borde punteado.
- Insignias de estado: Pendiente (ámbar), Completado (verde), Fallido (rojo suave), Vencido y Cancelado (gris).
- Pantallas de éxito: fondo verde `#1E7A36`, texto blanco.
- Montos con números tabulares; los montos son el elemento más grande de cada vista.
- Enlace de regreso "← Caja" arriba a la izquierda en las vistas internas del corresponsal.
- Ancho máximo de contenido: 560 px en vistas operativas, 1240 px en historial y admin.
```

</details>

### 3. Rama y PR del theme

> crea una rama feat/theme y envia estos cambios con pr a min

### 4. Validar contra la infraestructura

> puedes validar que la app cumpla con la siguiente infraestructura

<details>
<summary>Infraestructura pegada</summary>

```
Infraestructura Angular solo front (sin BFF)
Proyecto

* Una sola app Angular, sin monorepo ni workspaces.
* Scripts:
   * `start`: `ng serve --host 127.0.0.1 --port 4200`
   * `build`: `ng build`
   * `typecheck`: `ngc --noEmit -p tsconfig.app.json`
* Dependencias: `@angular/*` 19, `primeng` 19, `@primeuix/themes`, `primeicons`, `rxjs`, `zone.js` y `zod`. Si no vas a validar las respuestas, `zod` sobra.

Angular

* Todo es standalone y arranca con `bootstrapApplication(AppComponent, appConfig)`.
* Usa zone.js, signals, control flow (`@if`/`@for`) y formularios reactivos tipados (`nonNullable`).
* Builder: `@angular-devkit/build-angular:application` (esbuild). En la sección `schematics`, los componentes salen standalone y con SCSS.
* Presupuestos de producción: el bundle inicial avisa en 750kB y falla en 1MB; el SCSS de cada componente avisa en 8kB y falla en 12kB. Se usa `outputHashing: all`.
* TypeScript estricto: `strict`, `strictTemplates`, `strictInjectionParameters`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`.
* UI con PrimeNG y el tema `Aura`. El modo oscuro se activa con `.app-dark`, el ripple está apagado y los iconos son `primeicons`.
* Los design tokens van como variables CSS en `:root`, dentro de src/styles.scss.

Estructura de `src/`

app/        app.component.ts (shell), app.config.ts, app.routes.ts
core/       services/, interceptors/, guards/
shared/     schemas Zod / tipos / componentes reutilizables
features/   <feature>/<feature>.component.{ts,html,scss}
environments/ environment.ts, environment.development.ts

* Todas las rutas son lazy con `loadComponent`, y `**` lleva a NotFound.
* Los servicios son `providedIn: 'root'` y usan `inject(HttpClient)`. Validan las respuestas con Zod y les ponen `timeout`.
* Las suscripciones se limpian con `takeUntilDestroyed(destroyRef)`.

Qué reemplaza al BFF

* URL de la API: pon `environment.apiUrl` y configura `fileReplacements` en `angular.json`. Otra opción es una ruta relativa `/api` que nginx redirige al backend.
* Proxy en desarrollo: `proxy.conf.json` con `"/api/**": { "target": "https://api-dev...", "changeOrigin": true }`. Así evitas problemas de CORS en local.
* Autenticación:
   * Lo ideal es que el backend emita una cookie `HttpOnly; Secure; SameSite`. El front solo usa `withCredentials: true` mediante un interceptor funcional en `provideHttpClient(withInterceptors([...]))`.
   * Si el backend solo devuelve un token, guárdalo en memoria (un signal) y mándalo como `Authorization: Bearer`. Nunca lo guardes en `localStorage`.
* CSRF: si usas cookies, agrega `withXsrfConfiguration({ cookieName, headerName })`.
* Protección de rutas: un `canActivate` funcional en `core/guards/`. El guard es solo UX; la seguridad real la pone el backend.
* Errores: un interceptor que mande a `/login` cuando recibe un 401.
```

</details>

### 5. Alinear a la infraestructura en Angular 21

> Alinea la app a la especificación pero quédate en Angular 21: quita SSR, agrega zone.js, PrimeNG Aura, SCSS con tokens en :root, Zod, environments, proxy, interceptores, guard, estructura core/shared/features y ajusta scripts, budgets y schematics.

### 6. Commit y PR de la infraestructura

> Commitea en feat/infra-spa y abre PR a main; incluye el commit del theme si aún no se ha mergeado el PR 2.

### 7. A1. Login

> procedamos con la creacion del login

<details>
<summary>Especificación A1</summary>

```
## A1. Login

**Propósito:** entrada única para corresponsal y admin.
**Se llega:** al abrir el prototipo y al cerrar sesión.

**Estructura (tarjeta centrada, ancho máx. 400 px):**
1. Wordmark "relia." (Goldplay Alt, 40 px, cobalto con punto coral).
2. Subtítulo: "Tu caja de efectivo y dinero digital."
3. Campo **Correo** (precargado `rosa@demo.relia`).
4. Campo **Contraseña** (precargado `demo1234`).
5. Mensaje de error (solo si aplica).
6. Botón principal **Entrar**.
7. Ayuda: "Corresponsal: rosa@demo.relia / demo1234" y "Admin Relia: admin@relia.co / admin1234".
8. Enlace "¿Eres usuario? Vincula tu celular" → C1.

**Validación y destino:**
- `rosa@demo.relia` + `demo1234` → B1 Caja.
- `admin@relia.co` + `admin1234` → D1 Inicio admin.
- Otro valor → "Correo o contraseña incorrectos." (fondo rojo suave).

**Teclado:** Enter envía el formulario.
```

</details>

### 8. B0. Encabezado y menú (corresponsal)

<details>
<summary>Especificación B0</summary>

```
## B0. Encabezado y menú (corresponsal)

**Presente en:** Caja, Depositar, Retirar, Cargar cupo, Efectivo en caja, Historial.
**Estructura:** barra blanca fija arriba.
- Izquierda: wordmark "relia." + "Tienda Doña Rosa". Al tocarlo → Caja.
- Derecha: botón de menú (ícono de tres líneas, 44 × 44 px).

**Menú desplegable:**
- Rosa Gómez / rosa@demo.relia (informativo).
- Historial → B8.
- Efectivo en caja → B7.
- Cerrar sesión (texto rojo) → A1; limpia cualquier flujo en curso.
```

</details>

### 9. B1. Caja (inicio)

<details>
<summary>Especificación B1</summary>

```
## B1. Caja (inicio)

**Propósito:** ver saldos de un vistazo y lanzar operaciones.
**Se llega:** tras el login, desde el wordmark o "← Caja".

**Estructura:**
1. **Aviso cupo bajo** (si cupo < $50.000): banner ámbar "Cupo bajo, carga cupo para seguir depositando."
2. **Aviso efectivo bajo** (si efectivo < $50.000): banner ámbar "Efectivo bajo, asigna más para pagar retiros." + enlace "Asignar efectivo".
3. **Tarjeta cobalto:**
   - "Cupo disponible" + monto grande (38 px, blanco).
   - Separador.
   - "Comisión acumulada" + monto en amarillo.
4. **Tarjeta Efectivo en caja:** etiqueta + monto + botón secundario "Asignar efectivo" → B7.
5. **Acciones (cuadrícula 2 × 2):**
   - **Depositar** (coral, flecha ↓) → B2.
   - **Retirar** (cobalto, flecha ↑) → B3.
   - **+ Cargar cupo** (blanco) → B6.
   - **Historial** (blanco) → B8.
6. **Últimas operaciones:** título + enlace "Ver todo" → B8.
   - Hasta 3 filas: tipo, hora · identificador, valor, insignia de estado si no está completada.
   - Vacío: "Aún no hay operaciones. Empieza asignando efectivo y cargando cupo."
```

</details>

### 10. B2.1. Depositar: formulario

<details>
<summary>Especificación B2.1</summary>

```
## B2. Depositar

**Propósito:** el usuario entrega efectivo y recibe dinero en su billetera.
**Se llega:** acción "Depositar" en Caja o "Nueva operación" tras un depósito.

### B2.1 Formulario (una sola vista)
1. "← Caja".
2. Título **Depositar** + subtítulo "El usuario entrega efectivo y recibe dinero en su billetera."
3. Tarjeta blanca con:
   1. **Celular del usuario**: campo numérico, formato `300 000 0001`, máx. 10 dígitos. Cambiarlo borra la validación previa.
   2. Mensajes de llave (ver B2.2).
   3. **Monto**: campo grande con prefijo `$`, separador de miles automático.
   4. Casilla **"Que el usuario reciba el valor redondo"** con la ayuda "La comisión de $1.000 se cobra aparte."
   5. Error de monto (si 0 < M ≤ 1.000): "El monto debe ser mayor a $1.000."
   6. **Desglose** (si M > 1.000):
      - Bloque azul claro **"Dile al usuario"**:
        - Destacado: "Entrega en efectivo" + monto grande.
        - Recibe en su billetera.
        - Comisión Relia $1.000.
      - Bloque gris **"Para ti"**:
        - Recibes en efectivo.
        - Tu comisión +$500.
        - Cupo después.
        - Efectivo en caja después.
   7. Error de cupo (si el cupo no alcanza): "Cupo insuficiente, faltan $X" + enlace "Cargar cupo".
   8. Botón principal:
      - Antes de validar: **Validar llave** (activo solo con celular de 10 dígitos, monto válido y cupo suficiente).
      - Llave validada: **Confirmar depósito** (deshabilitado si el monto o el cupo no cumplen).
   9. Casilla **"Simular que la llave no existe"** + insignia Simulación (solo visible antes de validar).
```

</details>

### 11. B2.3. Confirmación del depósito

```
### B2.3 Confirmación
"Confirmar depósito" abre B4 con: *"¿Confirmas que recibiste en efectivo un total de $X por parte del usuario?"*
```

### 12. B2.4–B2.6. Enviando, completado y error

<details>
<summary>Especificación B2.4–B2.6</summary>

```
### B2.4 Enviando
Tarjeta centrada: indicador giratorio, "Enviando a la billetera", monto que recibirá la billetera, "No cierres esta pantalla." Dura 2 s.

### B2.5 Completado
Tarjeta verde:
- "Depósito completado".
- Monto enviado a la billetera (grande) + "llegaron a la billetera del usuario".
- Resumen: efectivo recibido, comisión Relia, cupo antes, cupo después, efectivo en caja antes → después, operación.
- Botones: **Nueva operación** (blanco), **Ver comprobante** (borde) → B9, "Volver a la caja" (enlace).
- Efecto: el usuario recibe un SMS simulado "Recibiste $X en tu billetera…".

### B2.6 Error
Tarjeta blanca con borde rojo:
- Título y texto según el caso:
  - **Error de red**: "El simulador no respondió. No se movió dinero. Intenta de nuevo."
  - **Cupo insuficiente**: "No hay cupo para este depósito. Carga cupo y vuelve a intentar."
- "Operación RL-XXXX · $M".
- Botones: **Intentar de nuevo** (vuelve al formulario con los datos) y "Volver a la caja".
```

</details>

### 13. B3. Retirar

<details>
<summary>Especificación B3</summary>

```
## B3. Retirar

**Propósito:** el usuario paga desde su billetera y recibe efectivo.
**Se llega:** acción "Retirar" en Caja.

### B3.1 Formulario
Igual que B2.1, con estas diferencias:
- Subtítulo: "El usuario paga desde su billetera y recibe efectivo."
- Desglose:
  - **Dile al usuario**: destacado "Recibe en efectivo" + monto; paga desde su billetera; comisión Relia $1.000.
  - **Para ti**: entregas en efectivo; tu comisión +$500; cupo después; efectivo en caja después.
- Error de efectivo: "Efectivo insuficiente, faltan $X" + enlace "Asignar efectivo" → B7.
- Botón final tras validar: **Enviar solicitud de pago**.

### B3.2 Esperando aprobación
Tarjeta centrada:
- Indicador giratorio.
- "Esperando que el usuario apruebe en su billetera".
- Monto que paga el usuario (grande).
- Contador regresivo desde **5:00** en píldora ámbar.
- Aviso rojo destacado: **"No entregues efectivo todavía"**.
- "Abrir teléfono del usuario" + insignia Simulación → C2.
- "Cancelar solicitud" (texto rojo).
- Efecto: llega un SMS al usuario y aparece la solicitud en "Mi billetera".

### B3.3 Completado (entrega)
Tarjeta verde:
- "Pago aprobado".
- **"Entrega en efectivo"** + monto muy grande (52 px).
- Resumen: pagó desde su billetera, comisión Relia, cupo antes/después, efectivo en caja antes → después, operación.
- Botón **Listo** → abre B4 con *"¿Confirmas que entregaste en efectivo un total de $X al usuario?"*; al confirmar → Caja.
- **Ver comprobante** → B9.

### B3.4 Error
Mismo formato que B2.6. Casos:
- **El usuario rechazó el pago**: "No se movió dinero. No entregues efectivo."
- **La solicitud venció**: "Se acabó el tiempo. No se movió dinero."
- **Solicitud cancelada**: "Cancelaste la solicitud. No se movió dinero."
- **El pago falló**: "La billetera no completó el pago. No se movió dinero."
- **Efectivo insuficiente** (al aprobar): "No tienes efectivo suficiente para este retiro. Asigna más efectivo."
```

</details>

### 14. B4. Modal de confirmación de efectivo

<details>
<summary>Especificación B4</summary>

```
## B4. Modal de confirmación de efectivo

**Propósito:** confirmación explícita del manejo de billetes.
**Estructura:** fondo cobalto translúcido; tarjeta centrada (máx. 420 px).
- Etiqueta "Confirma el efectivo".
- Pregunta (20 px):
  - Depósito: "¿Confirmas que recibiste en efectivo un total de $X por parte del usuario?"
  - Retiro: "¿Confirmas que entregaste en efectivo un total de $X al usuario?"
- **Sí, confirmo** (principal).
- **No, revisar** (secundario): cierra sin hacer nada.
```

</details>

### 15. B5. Modal "Esta llave no existe"

<details>
<summary>Especificación B5</summary>

```
## B5. Modal "Esta llave no existe"

**Propósito:** ofrecer invitar al usuario a vincular su billetera.
**Se abre:** al validar con "Simular que la llave no existe" activa, o con "Invitar por WhatsApp".

**Estructura:**
- Título "Esta llave no existe" + insignia Simulación.
- "El 300 000 0002 no tiene billetera vinculada. ¿Quieres enviarle una invitación por WhatsApp?"
- "Enlace generado": `https://wa.me/57XXXXXXXXXX?text=Hola%2C%20te%20invito…`
- "Mensaje": burbuja verde con *"Hola, te invito a vincular tu wallet en relia.com/selectkey"*.
- **Sí, enviar mensaje** (verde): cierra, marca como enviada y deja el mensaje en C2 con el botón "Abrir enlace para vincular".
- **Ahora no**: cierra.
```

</details>

### 16. B6. Cargar cupo

<details>
<summary>Especificación B6</summary>

```
## B6. Cargar cupo

**Propósito:** subir el saldo operativo del corresponsal.
**Se llega:** acción "Cargar cupo" en Caja o enlace desde el error de cupo.

**Encabezado:** "← Caja", título **Cargar cupo**, "Cupo actual $X".

### B6.1 Paso 1: método
Etiqueta "Paso 1 · Elige el método". Dos tarjetas seleccionables:
- **Bre-B**: "Genera un QR y paga desde cualquier banco. Hasta 10 minutos." + Simulación.
- **Wallet**: "Envía una solicitud a la billetera de tu llave y apruébala allí." + Simulación.

### B6.2 Paso 2: datos
Etiqueta "Paso 2 · Bre-B" o "Paso 2 · Wallet" + enlace "Cambiar método".
- **Monto a cargar** (prefijo `$`, miles automáticos).
- Solo Wallet: **Tu llave (celular)**, precargada `310 555 0101`, con la ayuda "Enviaremos la solicitud a la billetera vinculada a esta llave."
- Botón: **Generar QR** (Bre-B) o **Enviar solicitud** (Wallet). Activo con monto > 0 y, en Wallet, llave de 10 dígitos.

### B6.3 Esperando pago
- Insignia "Simulación Bre-B" o "Simulación Wallet".
- Bre-B: QR de ejemplo sobre fondo azul claro.
- Wallet: "Aprueba la solicitud en tu billetera" + "Llave 310 555 0101".
- Monto grande.
- Píldora ámbar con punto pulsante: "Esperando pago · m:ss" (10:00 Bre-B, 5:00 Wallet).
- "Confirma el pago desde el panel Modo demo."
- **Cancelar** (secundario).

### B6.4 Completado
Tarjeta verde: "Cupo cargado", "+$X", cupo antes y cupo después, operación, botón **Volver a la caja**.

### B6.5 Error
Tarjeta con borde rojo. Casos: **Pago rechazado** ("La carga no se completó. Tu cupo no cambió."), **La solicitud venció**, **Solicitud cancelada**. Botones **Intentar de nuevo** (vuelve al paso 2) y "Volver a la caja".
```

</details>

### 17. B7. Efectivo en caja

<details>
<summary>Especificación B7</summary>

```
## B7. Efectivo en caja

**Propósito:** definir el efectivo físico disponible para pagar retiros.
**Se llega:** tarjeta de efectivo en Caja, menú, aviso de efectivo bajo o error de efectivo insuficiente.

**Estructura:**
1. "← Caja", título **Efectivo en caja**, ayuda "Define cuánto efectivo tienes para pagar retiros. Los depósitos lo suben y los retiros lo bajan."
2. Tarjeta cobalto: "Efectivo disponible" + monto.
3. Tarjeta blanca:
   - Selector segmentado: **Sumar efectivo** | **Fijar total**.
   - Campo de monto ("Efectivo que agregas" o "Total de efectivo en caja").
   - Fila "Efectivo después" con la vista previa.
   - **Guardar efectivo** (activo con monto > 0 al sumar, o cualquier monto al fijar).
   - Tras guardar: recuadro verde "Efectivo actualizado · $antes → $después · RL-XXXX" + "Volver a la caja".

**Registro:** fila "Asignación de efectivo" (sumar) o "Ajuste de efectivo" (fijar) en el historial; cupo y comisión no cambian.
```

</details>

### 18. B8. Historial

<details>
<summary>Especificación B8</summary>

```
## B8. Historial

**Propósito:** auditoría completa de saldos.
**Se llega:** acción Historial, "Ver todo" o el menú.

**Estructura:**
1. "← Caja", título **Historial**, resumen "Cupo $X · Comisión $X · Efectivo $X".
2. Ayuda: "Toca una operación para ver su comprobante."
3. Filtros en píldoras (desplazables): **Todas, Depósito, Retiro, Carga de cupo, Efectivo, Pago de comisión**. La activa va en cobalto.
4. Contenido:
   - **Escritorio (≥ 900 px):** tabla con columnas Fecha, Tipo, Valor, Cupo antes, Cupo después, Comisión antes, Comisión después, Efectivo antes, Efectivo después, Estado.
   - **Móvil:** tarjeta por operación con tipo, fecha, valor, insignia de estado y una cuadrícula 2 × 3 con los seis saldos antes/después.
   - Operaciones pendientes muestran "Pendiente" en los saldos.
   - Vacío: "No hay operaciones de este tipo. Cuando hagas una, aparecerá aquí."
5. Tocar una fila o tarjeta → B9.

Orden: más reciente primero.
```

</details>

### 19. B9. Comprobante

<details>
<summary>Especificación B9</summary>

```
## B9. Comprobante

**Propósito:** soporte de la operación para corresponsal, usuario o admin.
**Se abre:** "Ver comprobante" en pantallas de éxito o tocando una operación en B8 o D2.

**Estructura (modal, máx. 400 px):**
1. Tarjeta blanca:
   - Wordmark "relia." + insignia de estado (color según estado).
   - "Comprobante de {tipo}" + monto principal grande + leyenda:
     - Depósito: monto enviado, "enviados a la billetera del usuario".
     - Retiro: efectivo, "entregados en efectivo".
     - Carga: "cargados a tu cupo".
     - Efectivo: "agregados a la caja" o "nuevo total en caja".
     - Pago de comisión: "pagados en comisiones".
   - Separador punteado.
   - Filas etiqueta / valor:
     - Siempre: Fecha, Operación.
     - Depósito/retiro: Celular (`300 *** 0001`), Billetera (oculta), efectivo entregado/recibido, enviado/pagado desde la billetera, Comisión Relia, Modalidad ("Comisión incluida" o "Valor redondo (comisión aparte)").
     - Carga: Método, Cupo antes, Cupo después.
     - Efectivo: Efectivo antes, Efectivo después.
     - Pago de comisión: Destino, Comisión antes, Comisión después.
     - Siempre: Estado; Motivo si falló.
   - Pie: "{Corresponsal} · Corresponsal Relia. Guarda este recibo como soporte de tu operación."
2. Botones bajo la tarjeta:
   - **SMS · Simulación** (solo depósito y retiro): envía el comprobante al teléfono del usuario; cambia a "SMS enviado".
   - **Imprimir**.
   - **Cerrar** (principal).
```

</details>

### 20. D1. Inicio admin

<details>
<summary>Especificación D1</summary>

```
## D1. Inicio admin

**Propósito:** visión global de la red y gestión de comisiones.

**Estructura (ancho máx. 1240 px):**
1. Título **Corresponsales**.
2. **Indicadores** (cuadrícula adaptable):
   - **Total dinero movido** (tarjeta cobalto): suma de depósitos y retiros completados + número de operaciones.
   - **Depósitos**: suma + cantidad.
   - **Retiros**: suma + cantidad.
   - **Comisiones por pagar** (coral).
   - **Comisiones pagadas hoy** (verde).
3. **Tabla de corresponsales** (desplazamiento horizontal en móvil):
   - Corresponsal (nombre como **enlace** → D2, responsable debajo).
   - Ciudad, Operaciones, Depósitos, Retiros, Total movido, Cupo, Efectivo.
   - Comisión por pagar (coral, negrita).
   - Acción: **Pagar comisión** → D3, o "Sin comisión".
4. **Pagos de comisión**: lista con corresponsal, hora · identificador PG-XXX · pointer, monto. Vacío: "Aún no has pagado comisiones hoy."

Tienda Doña Rosa refleja en vivo lo que se opera en la app del corresponsal.
```

</details>

### 21. D0. Encabezado admin

```
## D0. Encabezado admin

Barra cobalto fija: wordmark blanco + insignia amarilla "Admin" (al tocarlo → D1). Derecha: **Cerrar sesión** → A1.
```

### 22. Registro de prompts

> crea una nueva rama y en base a docs/jeff_claude y AI_USAGE.md agregar a docs/oscar_claude la lista de proms realizados para frontend
