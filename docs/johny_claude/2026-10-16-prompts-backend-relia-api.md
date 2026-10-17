# Prompts: backend Relia API (.NET)

Prompts base usados con Claude Code para especificar, implementar y revisar el backend .NET de Relia.
Rama: `feature/relia-api`. Solo se incluyen los prompts que definieron el trabajo; se omiten preguntas
sueltas y confirmaciones.

Se configuro Claude con skills de desarrollador, revision de contexto, entendimiento de reglas de negocio, seguimiento de buenas practicas (SOLID, KISS), 
aclarar dudas antes de tomar desiciones importantes de arquitectura, proponer arquitectura simple, escalable y mantenible, definir claros nombres de convenciones,
considrear seguridad y performnance.

---

## 0. Creación del proyecto y su estructura

**Prompts**

```text
crea la carpeta backend, luego crea una proyecto API con .Net 10, se llamará relia api y debe incluir swagger.
si tienes alguna duda pregunta antes de modificar. Agrega el archivo gitignore y adicionale los archivos no necesarios para para un despliegue de proyecto o relacionado a configuraciones locales.
```

**Decisiones tomadas en las preguntas de seguimiento**

- Nombre del proyecto: `Relia.Api` (como el repo).
- Estilo: controladores (`dotnet new webapi --use-controllers`), .NET 10.
- `.csproj` directo en `backend/API`, sin solución.
- Swagger UI (`Swashbuckle.AspNetCore.SwaggerUI`) sobre el OpenAPI nativo, solo en Development; abre en
  `/swagger` al ejecutar (`launchSettings.json`).
- Con un backend Node al lado: un `.gitignore` por proyecto y nombres de carpeta descriptivos en vez de `API`.

**Resultado:** `backend/API/Relia.Api.csproj` y `backend/.gitignore`.

## 1. Especificación

**Prompt**

```text
/to-spec contexto: @contracts/FLUJOS.md @contracts/rail-proxy.openapi.yaml @contracts/relia-api.openapi.yaml
@docs/wayfinder/contratos-api/ revisa los documentos para implementar los contratos especificados en estos.
no modifiques codigo aun.
```

**Decisiones tomadas en las preguntas de seguimiento**

- Alcance: solo el backend .NET.
- Base de datos: PostgreSQL (esquema v0.2, `docs/relia_schema_hackathon_v0.2.sql`).
- Costuras de prueba: pruebas HTTP con `WebApplicationFactory`, riel falso (`IRailClient`) y reloj inyectable.
- Tracker: markdown local en `docs/wayfinder/relia-api/issues/`.

**Resultado:** `docs/wayfinder/relia-api/issues/01-implementar-backend-relia-api.md`.

## 2. Tickets

**Prompt**

```text
/to-tickets
```

**Resultado:** tickets 02–17 en `docs/wayfinder/relia-api/issues/` (login, cliente del Rail Proxy, caja e
historial, depósito, retiro, carga de cupo, recibos, liquidaciones, modo demo, guion de demo).

## 3. Esqueleto: endpoints, servicios y repositorios

**Prompt**

```text
incialmente crea los endpoints que consume el corresponsal, con los servicios, debe ser pattern repository
y por el momento la implementacion del servicio retorna el texto "en desarrollo".
```

**Decisiones:** respuesta `501` problem+json "en desarrollo"; un solo proyecto con carpetas; EF Core con Postgres.

**Resultado:** controladores → servicios → repositorios → `ReliaDbContext`.

## 4. Scaffolding de la base de datos

**Prompt**

```text
agrega los paquetes necesarios de entityframework para hacer scaffolding que se hara apuntando a una base de
datos PostgreSQL
```

**Resultado:** entidades generadas desde la base real; mapeo manual de los enums de Postgres en
`Persistence/ReliaDbContext.Enums.cs` (el scaffold no los genera).

## 5. Implementación de los tickets

**Prompts**

```text
/implement los tickets que falten
```

**Regla de trabajo dada:** `solo haz commit cuando de la instruccion`.

**Resultado**

- Toda la Relia API implementada: corresponsal, vinculación con OTP, admin, liquidaciones y modo demo.
- Cliente HTTP del Rail Proxy (`Rail/HttpRailClient.cs`).
- Máquinas de estado de operación y liquidación, y barrido de fondo cada 15 s.
- Pruebas por ticket en `backend/Relia.Api.Tests/Tickets/`. Cada respuesta se valida contra
  `contracts/relia-api.openapi.yaml`.


## 7. Creación del README

**Prompt**

```text
/research @backend/ revisa el proyecto y haz un readme fácil de entender y que tenga el mismo formato de
@rail-proxy/README.md
```

**Resultado:** `backend/README.md` reescrito con el formato de `rail-proxy/README.md`: requisitos,
configuración (tabla de variables), cómo correr (Swagger en `/swagger`), datos de la demo, base de datos,
pruebas, archivos clave, flujos y errores. Cada dato se tomó del código del proyecto y se conservó lo vigente
del README anterior.


