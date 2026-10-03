# Template full-stack con módulos por feature y Dune ejecutable

Estado aceptado para implementación. Este ADR fija el objetivo de la copia. No afirma que el código ya lo implemente ni que el backend esté verificado.

El template heredado usa Wolverine sobre EF Core e Identity. No usa mensajería externa. Sus ADR de autorización y perfil están aceptados, pero el código no los implementa. Elegimos módulos directos por feature para hacer explícitos los commits, concentrar las reglas de cada concepto y eliminar el bus, la generación dinámica y la persistencia de mensajes sin consumidor real.

## Uso del consumidor

El frontend usa `/api/v1` del mismo origen mediante proxy. El usuario se registra, inicia sesión, edita su perfil y opera sus Tasks. Las respuestas de error usan ProblemDetails. La autorización diferencia recurso inexistente de recurso ajeno.

- `POST /auth/register` recibe `userName`, `email`, `password` y `displayName`. Crea identidad, perfil y sesión en una transacción. Devuelve 201 con usuario, access token y vencimiento. El perfil empieza en `UTC`.
- `POST /auth/login` recibe `userName` y `password`. Devuelve 200 con el mismo contrato. Credenciales incorrectas devuelven 401 sin revelar cuál falló. Registro y login también exigen antiforgery para impedir login CSRF.
- `GET /auth/csrf` entrega el token antiforgery en la cookie legible `XSRF-TOKEN`.
- `POST /auth/refresh` rota la sesión de la cookie HttpOnly y devuelve access token y vencimiento. No recibe ni devuelve el refresh token en JSON.
- `POST /auth/logout` revoca la sesión actual y borra su cookie. No necesita un access token vigente. Refresh y logout exigen el header antiforgery.
- `GET /users/me` devuelve `authId`, `userName`, `email`, `displayName`, `timezone`, roles y permisos vigentes.
- `PUT /users/me` cambia `displayName` y `timezone`. Rechaza una zona que no sea IANA válida.
- `GET /users/{authId}` y `DELETE /users/{authId}` mantienen la superficie heredada con autorización owner-or-permission. El borrado cascadea perfil, Tasks y sesiones.
- `POST /tasks` recibe `title` y devuelve 201 con `id`, `title`, `isCompleted` y fechas.
- `GET /tasks?page=1&pageSize=20` lista solo Tasks propias y devuelve `items`, `totalCount`, `page` y `pageSize`. Un permiso global no convierte esta lista personal en una lista administrativa.
- `GET /tasks/{id}` y `PUT /tasks/{id}` devuelven el recurso. El PUT recibe `title` e `isCompleted` como estado final.
- `DELETE /tasks/{id}` devuelve 204.
- `GET /health/live` comprueba solo el proceso. `GET /health/ready` comprueba PostgreSQL y migraciones. Readiness devuelve 503 ante una dependencia no disponible.

Para cada recurso, el módulo carga por Id. Si falta, devuelve 404. Después decide owner-or-permission. Si existe pero no permite acceso, devuelve 403. No combina Id y propietario para ocultar estos estados.

## Modelo y estructura

`UserProfile` es el perfil del usuario. Contiene `AuthId`, `DisplayName` y `Timezone`. Identity conserva el nombre de login y el email. `TaskItem.OwnerId` referencia `UserProfile.AuthId`. La cadena de borrado es Identity → UserProfile → TaskItem. Las sesiones referencian Identity. No existe un segundo `User` ni un `new DbSet` que oculte `Identity.Users`.

Cada feature es un module. Su interface pública HTTP oculta validación, reglas, autorización y persistencia. Su implementation usa un módulo concreto invocado por endpoints mínimos, sin mediator ni repositorio genérico. Un seam compartido de permisos evita repetir la política runtime. No creamos interfaces para módulos con un solo adapter.

```text
backend/
  Features/
    Auth/      endpoints, módulo, contratos, Domain/, Persistence/, Tests/
    Users/     endpoints, módulo, contratos, Domain/, Persistence/, Tests/
    Tasks/     endpoints, módulo, contratos, Domain/, Persistence/, Tests/
    Health/    endpoints, probes, Tests/
  Shared/      autorización y errores compartidos con uso real
  Infrastructure/
    Persistence/  IdentityDbContext y migraciones
    Identity/     implementación de Identity y permisos
    Observability/
  Tests/       proyecto de tests y soporte de Postgres/WebApplicationFactory
  Program.cs
  TemplateApp.csproj
tools/Dune.Analyzers/  analizadores semánticos y pruebas positivas/negativas
tools/                 controles de estructura y escenarios HTTP
frontend/src/app/features/{auth,users,tasks}/
openspec/              única fuente activa de contratos full-stack
docs/adr/              decisiones de toda la copia
```

Los tests viven físicamente junto a la feature. El proyecto de producción los excluye. Un proyecto de tests los incluye por ruta. El soporte de runtime compartido no pertenece a una feature. Los modelos puros de cada feature no dependen de ASP.NET, EF, Identity, Serilog ni OpenTelemetry. Las configuraciones EF viven junto a la feature, fuera de `Domain`.

Los endpoints solo adaptan HTTP y llaman a su módulo. Los módulos pueden usar EF e Identity directamente. Infrastructure puede conocer modelos y configuraciones de persistencia, pero no endpoints ni módulos de aplicación. Una feature no llama a la implementation de otra. Las referencias necesarias a modelos de perfil y contratos compartidos son explícitas. `Program` es el único punto de composición.

## Consistencia y seguridad

PostgreSQL es la base real. EF Core e Identity comparten el mismo contexto scoped. El registro abre una transacción explícita antes de las escrituras de Identity y la confirma después del perfil, rol y sesión. Un retorno de error sale sin commit. Un índice único resuelve carreras de email y nombre, no solo una lectura previa. Las operaciones de una sola escritura usan el commit de EF. Las migraciones nuevas conservan los datos del esquema heredado y cambian sus nombres con backfill, no recrean la base.

El access token contiene identidad, identificador de sesión `sid` y claims estándar de vigencia. No contiene roles ni permisos. La validación comprueba que esa sesión siga activa para que logout y replay invaliden también el access token. La autorización consulta los permisos vigentes en cada request. La implementación puede memorizar lecturas solo dentro de ese request. No necesita invalidación entre procesos ni una cache global que contradiga el próximo request.

El refresh token es aleatorio, opaco y se guarda como hash. La rotación usa una transacción y una actualización condicional para que solo una redención gane. Reutilizar un token rotado revoca la familia de esa sesión. No intentamos deducir si el reuso fue benigno o malicioso. El frontend coordina una sola renovación pendiente por pestaña.

La cookie de refresh usa HttpOnly, SameSite y Secure fuera del desarrollo local. Los endpoints que la consumen exigen antiforgery. El access token vive solo en memoria. No guardamos credenciales ni tokens en localStorage ni sessionStorage. Al recargar, Angular restaura la sesión por refresh. No registra request bodies, Authorization, Cookie ni tokens.

Tasks usa PUT con estado final en lugar de toggle. Dos toggles concurrentes con lectura y escritura pueden perder cambios. El estado final explícito evita enseñar una operación no idempotente innecesaria. El cliente migra completamente. No queda alias de toggle.

## Observabilidad y operación

Serilog escribe logs estructurados y los exporta por OTLP. OpenTelemetry exporta métricas y tracing. Los logs de request contienen los identificadores de trace y span, sin datos de autenticación. ASP.NET, HttpClient, runtime y PostgreSQL aportan señales disponibles. Las métricas de negocio usan etiquetas acotadas, no IDs de usuario ni títulos de Tasks.

Docker Compose ejecuta PostgreSQL con volumen, collector OpenTelemetry, API y frontend. El API corre sin root. Readiness comprueba base y migraciones sin exponer connection strings. Un fallo de arranque produce salida no cero. El collector tiene configuración reemplazable para un destino externo. El runtime smoke debe observar las tres señales exportadas, no solo su registro en DI.

Usamos las versiones estables actuales verificadas en registros. SDK .NET 10.0.401, runtime y paquetes Microsoft 10.0.12, Npgsql EF 10.0.3, Serilog ASP.NET 10.0.0 y Angular 22.2.1. .NET 11 RC queda fuera. Los paquetes OpenTelemetry se fijan a la última estable de cada paquete, no se inventa un patch común. El lockfile de npm y las versiones de NuGet se entregan con el cambio.

## Dune y gates duros

Dune es el conjunto de restricciones ejecutables de esta copia. La documentación explica reglas, pero no las aplica.

- Un analyzer Roslyn comprueba dependencias por símbolos y namespace. Prohíbe framework en Domain, endpoints accediendo a persistencia, Infrastructure invocando módulos de aplicación y llamadas entre implementations de features.
- Un analyzer comprueba que el namespace coincide con la ruta de la feature. Los nombres `*Module`, `*Endpoints`, `*Request`, `*Response`, `*Validator` y `*Tests` tienen un significado único.
- Un analyzer prohíbe shadowing de DbSet y el predicado que combina identidad del recurso con propietario en la carga de un recurso individual.
- BannedApiAnalyzers y EditorConfig prohíben APIs inseguras o bloqueantes concretas. Entre ellas, SQL sin parámetros, reloj local y bloqueo sincrónico de Task. Los alias y nombres calificados no evaden el chequeo por símbolos.
- El gate de estructura exige tests colocados por feature y evita que se compilen dentro del producto. Prohíbe residuos de Wolverine, artefactos versionados y fuentes OpenSpec duplicadas.
- Los diagnósticos Dune tienen severidad error en EditorConfig. CI no usa continue-on-error, no omite tests y no desactiva los analyzers.
- Cada gate se prueba con un caso válido y una violación deliberada. El escenario negativo debe observar el diagnóstico o el código de salida fallido. No inspecciona cadenas del código para fingir prueba de comportamiento.

CI ejecuta gates, build backend, tests con PostgreSQL real, build frontend y flujo HTTP del conjunto Docker. Las reglas de autorización, transacciones, rotación, cascadas y ausencia de permisos en JWT se prueban por comportamiento. No intentamos demostrar esas invariantes buscando el nombre de un método en el código.

Los PRs son atómicos por cambio observable. Un cambio de contrato lleva backend, front, tests y spec juntos. No imponemos un límite artificial de líneas. Este ADR se entrega como commit separado del backend, Dune y frontend. Un remoto ausente no se reemplaza con un enlace de PR inventado.

## Alternativas y síntesis

Se compararon dos diseños completos. La revisión independiente puntuó A con 12/30 y B con 20/30. El candidato A conservaba Wolverine, controllers parciales y transacciones automáticas. Reduce modificaciones heredadas, pero conserva el bus y el almacenamiento de mensajes sin un consumidor externo. También deja entidades separadas de la feature y tests en un espejo remoto. El candidato B usa módulos directos y transacciones explícitas. Lo elegimos por locality y por una interface que no obliga al mantenedor a aprender generación dinámica para completar una operación HTTP.

Del candidato A conservamos Identity, FluentValidation, ErrorOr, owner-or-permission y la cadena de cascadas. Del candidato B conservamos módulos concretos y transacciones explícitas. Corregimos ambos diseños para colocar tests físicamente, no persistir tokens en storage del navegador, no cachear permisos entre requests y no aceptar toggle con lost-update. Rechazamos los gates que inspeccionan texto incidental o que intentan inferir seguridad completa por una llamada presente.

## Relación con los ADR heredados

Se conservan la semántica owner-or-permission, el orden 404/403, JWT de identidad pura, formato de permisos, perfil separado, login estricto con `Auth.ProfileMissing` y cascadas de los ADR-0001 y ADR-0002 del backend.

Este ADR sustituye sus mecanismos de transacción ambient Wolverine por transacciones explícitas. Sustituye la cache global con invalidación por consulta scoped por request. La política del próximo request se conserva también en varias instancias. El requirement genérico de autorización puede mantenerse como adapter ASP.NET del seam compartido, sin obligar a introducir otro bus.

## Comprobación y riesgos

Antes de terminar, el runtime debe probar registro, login, restauración y cierre de sesión, perfil, Tasks, paginación, recurso ajeno 403, recurso ausente 404, permiso elevado sobre recurso ajeno, cambio de permiso visible en el siguiente request, rotación/reuso, registro duplicado sin filas parciales y cascadas. Debe demostrar liveness sin base, readiness fallido y exportación de logs, métricas y spans correlacionados. Angular debe completar el flujo contra la API real.

El SDK está instalándose. No se ejecuta dotnet ni se compila backend antes del aviso del usuario. Los analizadores y el backend quedan sin verificación de compilación hasta entonces. Los commits de implementación no se presentan como verificados. Los gates todavía pueden requerir ajustes tras las primeras compilaciones. La revisión de seguridad no convierte este template en un sistema de identidad listo para todos los productos.
