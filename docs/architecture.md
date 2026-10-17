# Arquitectura de TemplateApp

La decisión vigente es [ADR-0004](adr/0004-vertical-slices-deep-modules.md): vertical slices dentro de features que funcionan como módulos profundos. Las reglas para escribir código están en [conventions.md](conventions.md).

## Forma del backend

El backend es un solo proyecto ASP.NET Core 10, con namespace raíz `TemplateApp`.

```text
backend/
├── Program.cs               host: plataforma + lista explícita de features
├── Infrastructure/          fontanería técnica; no nombra ninguna feature
├── Shared/                  núcleo mínimo; no nombra ninguna feature
└── Features/
    └── Tasks/
        ├── TasksFeature.cs  registra DI y rutas de la feature
        ├── Contracts/       lo único que ven otras features
        ├── Domain/          modelos puros y reglas del dominio
        ├── Persistence/     mappings de EF
        └── CreateTask/
            ├── CreateTask.cs        request, validación, operación y endpoint
            └── CreateTaskTests.cs
```

- **Feature:** un módulo profundo. Su interfaz pública es `Contracts/` más sus rutas HTTP.
- **Slice:** un caso de uso. Es una clase propia, con sus dependencias.
- **Domain:** sin EF, ASP.NET ni infraestructura. Concentra las reglas que comparten las slices.
- **Infrastructure:** observabilidad, middlewares, arranque de la base y composición del host.
- **Shared:** tipos generales que usan varias features, como `ApiResults`.

No hay mediator ni bus. El paso 25 del plan elige uno.

## Estado actual frente al objetivo

El código todavía no tiene la forma de arriba. La Fase 0 del [plan](evolucion/plan.md) lo lleva hasta ahí.

| Diferencia | Paso que la resuelve |
|---|---|
| Código de features en `Shared/` e `Infrastructure/` (JWT, `AppIdentityUser`, permisos, roles, registro central) | 0.3 |
| Código muerto: `ITemplateMetrics`, `ValidationExceptionHandler` | 0.3 |
| `Tasks` usa partials de `TasksModule`; `Users`, `Auth` y `Health` no tienen slices | 0.4 |
| Auth y Users se usan por dentro en las dos direcciones | 0.5 |
| Los servicios de Auth y Users del frontend están en `core/` | 0.6 |
| Un solo `AppDbContext` para todas las features | Paso 22 |

## Perfil y propiedad

Identity guarda el nombre de login, el email y las credenciales. `UserProfile` guarda `AuthId`, `DisplayName` y `Timezone`. `TaskItem.OwnerId` referencia `UserProfile.AuthId`. La cascada va de Identity al perfil y del perfil a las tareas. Las sesiones referencian Identity.

El registro crea identidad, perfil, rol y sesión en una transacción explícita. Las restricciones únicas protegen las carreras. Un resultado de error no confirma la transacción. Las migraciones son forward: renombran y hacen backfill, sin reset de base ni `EnsureCreated`.

## Autorización y sesión

- Una operación carga el recurso por Id. Si no existe, devuelve 404. Después decide propietario o permiso, y si falla devuelve 403.
- Solo la lista personal filtra por propietario. Un permiso administrativo no amplía la lista personal.
- `User` recibe `Tasks.Create`. La propiedad habilita leer y modificar lo propio. `Admin` y `SuperAdmin` reciben permisos globales de Tasks. `SuperAdmin` recibe `Users.Manage`.
- Los permisos se consultan en cada request. El JWT lleva identidad y vigencia, nunca roles ni permisos.
- El refresh token es opaco. Se guarda como hash, con familia y vencimiento. La rotación es atómica. Reusar un token rotado revoca esa familia, no todas las sesiones del usuario.
- Register, login, refresh y logout validan antiforgery. El refresh vive en una cookie HttpOnly. Angular guarda el access token solo en memoria.
- `SessionHttpPolicy` es el adapter HTTP de la sesión: emite, lee, adjunta y borra cookies. `TokenService` no conoce HTTP; maneja el JWT y la rotación por familia.

## Frontend

Angular consume `/api/v1` del mismo origen, mediante un proxy. No guarda tokens en el storage del navegador. Cada feature tiene su cliente HTTP y sus modelos. `core/` queda para lo transversal.

## Operación

- Compose levanta PostgreSQL 18 con volumen, un collector OpenTelemetry, la API (sin root) y el frontend.
- Serilog exporta logs por OTLP. OpenTelemetry exporta métricas y trazas a `/v1/metrics` y `/v1/traces`.
- Los logs de request llevan trace y span, sin credenciales.
- Readiness comprueba PostgreSQL y las migraciones. Liveness no depende de la base.

## Dune

Dune hace cumplir esta arquitectura con analyzers Roslyn, símbolos prohibidos y gates de Node. Vive en `dune/`. La raíz solo lo conecta: `Directory.Build.props` importa `dune/Dune.props`, y `TemplateApp.slnx` y CI llaman a `dune/`.

Cada regla tiene una fixture válida y una violación deliberada. Las pruebas observan diagnósticos o códigos de salida, no búsquedas de texto.

Para comparar con el framework Dune de Lauren, leé [research-dune.md](research-dune.md).
