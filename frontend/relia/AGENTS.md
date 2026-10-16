
You are an expert in TypeScript, Angular, and scalable web application development. You write functional, maintainable, performant, and accessible code following Angular and TypeScript best practices.

## TypeScript Best Practices

- Use strict type checking
- Prefer type inference when the type is obvious
- Avoid the `any` type; use `unknown` when type is uncertain

## Angular Best Practices

- Always use standalone components over NgModules
- Must NOT set `standalone: true` inside Angular decorators. It's the default in Angular v20+.
- Use signals for state management
- Implement lazy loading for feature routes
- Do NOT use the `@HostBinding` and `@HostListener` decorators. Put host bindings inside the `host` object of the `@Component` or `@Directive` decorator instead
- Use `NgOptimizedImage` for all static images.
  - `NgOptimizedImage` does not work for inline base64 images.

## Accessibility Requirements

- It MUST pass all AXE checks.
- It MUST follow all WCAG AA minimums, including focus management, color contrast, and ARIA attributes.

### Components

- Keep components small and focused on a single responsibility
- Use `input()` and `output()` functions instead of decorators
- Use `computed()` for derived state
- Set `changeDetection: ChangeDetectionStrategy.OnPush` in `@Component` decorator
- Prefer inline templates for small components
- Prefer Reactive forms instead of Template-driven ones
- Do NOT use `ngClass`, use `class` bindings instead
- Do NOT use `ngStyle`, use `style` bindings instead
- When using external templates/styles, use paths relative to the component TS file.

## State Management

- Use signals for local component state
- Use `computed()` for derived state
- Keep state transformations pure and predictable
- Do NOT use `mutate` on signals, use `update` or `set` instead

## Templates

- Keep templates simple and avoid complex logic
- Use native control flow (`@if`, `@for`, `@switch`) instead of `*ngIf`, `*ngFor`, `*ngSwitch`
- Use the async pipe to handle observables
- Do not assume globals like (`new Date()`) are available.
- Do not write arrow functions in templates (they are not supported).

## Services

- Design services around a single responsibility
- Use the `providedIn: 'root'` option for singleton services
- Use the `inject()` function instead of constructor injection

## Infraestructura (solo front, sin BFF)

- Angular 21 standalone con zone.js, signals, control flow y formularios reactivos tipados (`NonNullableFormBuilder`). Sin SSR.
- UI: PrimeNG con preset `ReliaPreset` (Aura + primario cobalto) en `src/shared/theme/`. Modo oscuro con `.app-dark`, ripple apagado, iconos `primeicons`.
- Estructura: `app/` (shell, config, rutas), `core/` (services, interceptors, guards), `shared/` (schemas Zod, tipos, theme, componentes reutilizables), `features/<feature>/<feature>.component.{ts,html,scss}`, `environments/`.
- Rutas siempre lazy con `loadComponent`; `**` lleva a NotFound.
- Servicios `providedIn: 'root'` con `inject(HttpClient)`, URL desde `environment.apiUrl`, `timeout(environment.httpTimeoutMs)` y respuesta validada con Zod (`import * as z from 'zod/mini'`; `import { z } from 'zod'` infla el bundle ~400kB).
- Suscripciones con `takeUntilDestroyed(destroyRef)`.
- Auth: cookie HttpOnly (`credentialsInterceptor` → `withCredentials`) + XSRF; si el backend devuelve token, va solo en memoria (`AuthService`) como `Authorization: Bearer`. Nunca en `localStorage`. Un 401 manda a `/login`. `authGuard` es solo UX.
- En local, `proxy.conf.json` redirige `/api` al backend.

## Convenciones visuales (predefinidas por el equipo)

Los tokens son variables CSS en `:root` y las clases viven en `src/styles.scss`. Úsalos en vez de colores sueltos.

- Fondo de página gris claro `#F5F5F5` (`--relia-page`, ya aplicado al `body`); tarjetas blancas con radio 16 a 24 px (`card`, `card card-lg`).
- Botón principal: píldora cobalto `#2E3192`, texto blanco, alto 52 px (`btn-primary`). Deshabilitado: gris `#E8E8E8` con texto `#707070` (automático con `disabled`).
- Botón secundario: píldora blanca con borde gris; al pasar el cursor el borde se vuelve cobalto (`btn-secondary`).
- Insignia **Simulación**: píldora amarilla clara `#FDF1C4`, texto `#5C4700`, borde punteado (`badge-sim`).
- Insignias de estado: Pendiente ámbar (`badge-pending`), Completado verde (`badge-completed`), Fallido rojo suave (`badge-failed`), Vencido y Cancelado gris (`badge-muted`).
- Pantallas de éxito: fondo verde `#1E7A36`, texto blanco (`screen-success`).
- Montos con números tabulares; los montos son el elemento más grande de cada vista (`amount`).
- Enlace de regreso "← Caja" arriba a la izquierda en las vistas internas del corresponsal (`link-back`).
- Ancho máximo de contenido: 560 px en vistas operativas (`container-ops`), 1240 px en historial y admin (`container-wide`).
