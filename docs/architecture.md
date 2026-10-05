# Arquitectura de TemplateApp

La decisión vigente es [ADR-0003](adr/0003-full-stack-template-dune.md). Verificación real 2026-10-05: backend 24/24 pass (reporte parent artifact://611); Docker `up --build` final con éxito; smoke `--compose` exit 0 con live 200, ready 200/503 tras caída de `db`, reinicio con persistencia y OTLP `correlatedLogs true` + `httpDurationMetric true`; navegador PASS con register, create, dos reloads, checkbox y perfil persistentes, logout y login. Dune 22/22 PASS local (artifact://640); estructura 5/5 + check-structure OK + Angular build PASS. Gate duro real DUNE0001 con throwaway EF DbContext en Domain (`dotnet build` backend exit 1, `artifacts/compose-final/dune-negative-build.stdout.log`); throwaway removido y `dotnet build TemplateApp.slnx` exit 0, 0 errores (artifact://643). GitHub Actions no ejecutado; sin afirmación de cero warnings.

## Módulos directos por feature

El backend es un proyecto ASP.NET Core con namespace `TemplateApp`. `Features/Auth`, `Users`, `Tasks` y `Health` concentran cada capacidad. Endpoints mínimos adaptan HTTP y llaman módulos concretos. Un módulo oculta validación, reglas, autorización y persistencia detrás de su interface HTTP. No hay mediator, bus, transacciones Wolverine, repositorio genérico ni interfaces sin segundo adapter.

`Domain` dentro de cada feature contiene modelos puros. `Persistence` contiene mappings EF. `Contracts` contiene requests y responses. `Tests` contiene pruebas físicas de esa feature, compiladas por `backend/Tests/TemplateApp.Tests.csproj` y excluidas del producto.

`Shared/Authorization` contiene `IPermissionService`, `ResourceAuthorizer`, roles y permisos. `Shared/Http` adapta ErrorOr a ProblemDetails. `Infrastructure/Persistence` contiene el contexto Identity y las migraciones. `Infrastructure/Identity` contiene los adapters de identidad y permisos. `Infrastructure/Observability` configura señales operacionales.

`Program.cs` compone registro de infraestructura, observabilidad y health. La extensión `AddBackendInfrastructure` puede registrar módulos, pero Infrastructure no invoca su implementación. Una feature puede conocer Domain y Contracts de otra, no sus módulos ni endpoints.

## Perfil y propiedad

Identity conserva nombre de login, email y credenciales. `UserProfile` conserva `AuthId`, `DisplayName` y `Timezone`. `AppDbContext.UserProfiles` no oculta `Identity.Users`. `TaskItem.OwnerId` referencia `UserProfile.AuthId`. La cascada va de Identity al perfil y del perfil a Tasks. Sesiones referencian Identity.

La creación de identidad, perfil, rol y sesión ocurre en una transacción explícita. Identity y EF comparten el contexto scoped. Las restricciones únicas protegen carreras. Un resultado de error no confirma la transacción. Las migraciones forward conservan el esquema histórico y los datos mediante renombres y backfill. No hay reset de base ni EnsureCreated.

## Autorización y sesión

Un módulo carga el recurso por Id. Si no existe devuelve 404. Después `ResourceAuthorizer` decide propietario o permiso y devuelve 403 si falla. Solo la lista personal filtra por propietario. Un permiso administrativo no amplía la lista personal.

`User` recibe Tasks.Create, no permisos globales de lectura o modificación. La propiedad permite esas operaciones sobre su recurso. Admin y SuperAdmin reciben permisos Tasks globales. SuperAdmin recibe Users.Manage. `IPermissionService` consulta permisos vigentes por request sin cache entre procesos. JWT contiene identidad y vigencia, nunca roles ni permisos.

El refresh token es opaco y se persiste como hash con familia y vencimiento. La rotación es atómica. Reusar un token rotado revoca esa familia, no todas las sesiones del usuario. Register, login, refresh y logout validan antiforgery. Refresh vive en cookie HttpOnly. Angular guarda access token en memoria y coordina un único refresh pendiente por pestaña.

## Frontend y operación

Angular consume `/api/v1` del mismo origen mediante proxy. Auth, Users y Tasks tienen contratos full-stack en [OpenSpec](../openspec/specs/auth/spec.md). No se guardan tokens en storage del navegador.

Compose contiene PostgreSQL 18 con volumen, collector OpenTelemetry, API sin root y frontend. Backend Dockerfile usa `USER $APP_UID` (usuario `app` builtin, `adduser` ausente en .NET 10). Serilog exporta logs OTLP. OpenTelemetry exporta métricas y spans con endpoints base + `/v1/traces` y `/v1/metrics` porque el setter `Endpoint` no agrega ruta. Request logs incluyen trace y span sin credenciales. Readiness comprueba PostgreSQL y migraciones. Liveness no depende de la base. Migración única `InitialCreate` generada por CLI en commit `6687af8`, sin edición manual. Una configuración de exporters no es evidencia de recepción de señales; la evidencia 2026-10-05 es `artifacts/compose-final/compose-smoke-fixed.stdout.log` con OTLP `correlatedLogs true` + `httpDurationMetric true`.

## Dune y fuentes normativas

[Dune](../openspec/specs/dune/spec.md) aplica restricciones semánticas Roslyn, APIs prohibidas y estructura Node. Pruebas de reglas ejecutan fixtures válidos y negativos, no búsquedas de texto incidental. Las excepciones para migraciones históricas son exactas y justificadas.

[Convenciones](conventions.md) describe los nombres y las restricciones de contribución. [README](../README.md) contiene comandos de uso y gates. [CONTEXT](../CONTEXT.md) es solo glosario del dominio. [Archivo OpenSpec](../openspec/changes/archive/README.md) conserva evidencia y supersesiones. ADR-0001 y ADR-0002 conservan su historia con notas explícitas de los mecanismos sustituidos.
