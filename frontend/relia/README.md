# Relia (frontend Angular)

App web de Relia en Angular 21 con PrimeNG: la caja del corresponsal, el panel admin y las páginas públicas de vinculación. Consume la [Relia API](../../backend/README.md) según [contracts/relia-api.openapi.yaml](../../contracts/relia-api.openapi.yaml) y [contracts/FLUJOS.md](../../contracts/FLUJOS.md). Nunca llama al Rail Proxy. Estado de la integración: [docs/wayfinder/integracion-api/map.md](../../docs/wayfinder/integracion-api/map.md). Convenciones de código y visuales: [AGENTS.md](AGENTS.md).

## Requisitos

- Node 22.12 o superior.
- El [backend](../../backend/README.md) corriendo en `http://localhost:5080`.

## Correr

```sh
npm install
npm start          # http://localhost:4200
```

En local, [proxy.conf.json](proxy.conf.json) redirige `/api/*` a `http://localhost:5080` y le quita el prefijo `/api`. Si cambias el proxy, reinicia `npm start`.

Entra con un usuario de la demo (ver [Datos de la demo](../../backend/README.md#datos-de-la-demo)): `rosa@demo.relia` / `demo1234` (corresponsal) o `admin@relia.co` / `admin1234` (admin).

## Pantallas

| Ruta | Qué es |
|---|---|
| `/login` | Login de corresponsal y admin. |
| `/caja` | Caja del corresponsal: saldos y últimas operaciones. |
| `/caja/depositar`, `/caja/retirar`, `/caja/cupo` | Depositar, retirar y cargar cupo. |
| `/caja/historial`, `/caja/efectivo` | Historial y ajuste del efectivo en caja. |
| `/admin` | Panel de la red y pagos de comisión de hoy. |
| `/admin/corresponsal/:id`, `/admin/pagar/:id` | Detalle de un corresponsal y pago de su comisión. |
| `/vincular` | Página pública para vincular la billetera a un celular (OTP simulado). |
| `/telefono` | Teléfono simulado del usuario (SMS e invitaciones de la demo). |

## Configuración

[src/environments/](src/environments/): `apiUrl` (`/api`) y `httpTimeoutMs` (`15000`). En producción `/api` lo debe redirigir el servidor web al backend.

## Comandos

| Comando | Qué hace |
|---|---|
| `npm start` | Servidor de desarrollo en `127.0.0.1:4200`. |
| `npm run build` | Build de producción en `dist/`. |
| `npm run typecheck` | Chequeo de tipos de plantillas y TS. |
| `npm test` | Pruebas unitarias con Vitest. |

## Estructura

| Carpeta | Qué hace |
|---|---|
| [src/app/](src/app/) | Shell, configuración y rutas (todas lazy). |
| [src/core/](src/core/) | Servicios HTTP (validados con Zod), interceptores de auth y errores, guard. |
| [src/shared/](src/shared/) | Schemas, tipos, tema PrimeNG y componentes reutilizables. |
| [src/features/](src/features/) | Una carpeta por pantalla. |
