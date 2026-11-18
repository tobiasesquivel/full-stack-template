# Paso 22: un `DbContext` por feature (investigación)

- Fecha: 2026-11-18.
- Alcance: el paso 22 del [plan](../plan.md) (`plan.md:384-387`). Concepto: propiedad de datos; el contexto dios impide extraer módulos. Cambia: `Infrastructure/Persistence` hacia `Features/*/Persistence`, migraciones por contexto, arranque, tests. Verifica: suite completa con dos contextos; smoke con base limpia y migrada; readiness en verde. **Paso de alto riesgo:** la migración de esquema es irreversible en producción; se ensaya como en el paso 19 ([paso-19-expand-contract.md](paso-19-expand-contract.md), `backend/Tests/Persistence/TaskCompletionMigrationTests.cs`).
- Pendientes que este paso recibe (`progreso.md:98-100`): `AppIdentityUser` vive en `Auth/Contracts` solo porque `AppDbContext` hereda de `IdentityDbContext`; las FKs entre features; la atomicidad de `RegisterUser`. También `progreso.md:187`: el ensayo con binarios, que el paso 22 necesita.
- Decisiones ya tomadas (este informe no las reabre): EF Core 10 (`Microsoft.EntityFrameworkCore.Design` 10.0.12) con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:31-36`); PostgreSQL 18; la API migra al arrancar bajo un advisory lock (`DatabaseInitializer.cs:18-26`); migraciones generadas por la CLI y expand/contract (`conventions.md:92`, `:103-117`); un solo proyecto .NET con Dune (ADR-0004 punto 7); sin bus ni outbox hasta los pasos 24 y 25.
- Método: fuentes primarias (Microsoft Learn de EF Core, ASP.NET Core y System.Transactions; el código de `dotnet/efcore` en `release/10.0` y de `npgsql/efcore.pg` en `hotfix/10.0.4`; la documentación de Npgsql y PostgreSQL 18; los repos y guías de Kamil Grzybek, Milan Jovanović, ABP, Wolverine y eShop). Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA]. Lo que salió de una corrida mía va marcado [EXPERIMENTO].
- Evidencia propia. Todo corrió fuera del repo, en copias con `git archive HEAD` en el scratchpad (`C:\Users\Tobi\AppData\Local\Temp\claude\babba237ec9b8592\2e7de61d-a506-4817-8131-490e57c90636\scratchpad`). El repo solo suma este archivo (`git status` vacío al terminar). Un `postgres:18` (18.6) en Docker, en el puerto 47622; al final lo borré.
  - **EX-SEMILLA:** el binario de `main` (`HEAD`) migra una base vacía (`ex22_seed`). Un script registra 3 usuarios por HTTP y cada uno crea 3 tareas (una con `Idempotency-Key`) y completa una. Quedan 3 usuarios, 3 perfiles, 9 tareas, 3 sesiones, 3 claves de idempotencia y 15 filas de auditoría, con las 10 migraciones en `__EFMigrationsHistory`.
  - **EX-CONTROL:** el mismo binario de `main` corre `check.mjs` sobre una copia de la semilla: readiness, login, lista, crear con clave y repetirla, registrar a un usuario nuevo y borrar una cuenta. Es la referencia de lo que la API hace hoy.
  - **EX-ESQUEMA:** en una copia de `main`, `ToTable("TaskItems", "tasks")` y `dotnet ef migrations add`. La CLI genera `EnsureSchema("tasks")` y `RenameTable("TaskItems", newSchema: "tasks")`. El build falla con `DUNE0018 ... usa RenameTable`.
  - **EX-VISTA:** sobre una copia de la semilla, mover `TaskItems` a `tasks` con `ALTER TABLE ... SET SCHEMA` y dejar una vista `public."TaskItems"`. Después, probar `search_path` en vez de la vista.
  - **EX-SINFK:** en una copia de `main`, borrar `ConfigureCrossFeatureRelationships` y generar la migración con la CLI. Después correr `DeleteUserTests`, `AuditTrailTests` y `TaskCompletionMigrationTests`.
  - **EX-TX:** una consola con dos `DbContext` (EF 10.0.12, Npgsql 10.0.3) contra la misma base. Prueba cinco formas de una transacción entre contextos.
  - **EX-PARTIDO:** una copia con el diseño recomendado completo: `AuthDbContext`, `UsersDbContext`, `TasksDbContext` y `PlatformDbContext`, cada uno con su historial y su `InitialCreate` generado por la CLI; el `AppDbContext` viejo queda sin modelo y solo con sus migraciones; el arranque marca la línea base; `RegisterUser`, `DeleteUser` y el filtro de idempotencia usan `TransactionScope`.
  - **EX-HEREDADA:** el binario de EX-PARTIDO arranca sobre una copia de la semilla de `main` y corre `check.mjs`.
  - **EX-LIMPIA:** el binario de EX-PARTIDO arranca sobre una base vacía, siembra y corre `check.mjs`.
  - **EX-DIFF:** compara columnas (tipo, nulabilidad, default), restricciones e índices de la base de EX-HEREDADA contra la de EX-LIMPIA.
  - **EX-SUITE:** la suite de `backend/Tests` completa sobre EX-PARTIDO, con los tests adaptados solo en lo mecánico (el nombre del contexto).
  - **EX-VUELTA:** el binario viejo de `main` arranca sobre la base de EX-HEREDADA, ya migrada, y borra una cuenta.
  - **EX-DUNE:** tres violaciones plantadas en EX-PARTIDO: una feature que usa el contexto de otra, `Infrastructure` que usa el contexto de una feature y una feature que lee con SQL la tabla de otra.

## Glosario corto

- **`DbContext`** (contexto): la clase de EF Core que mapea tablas a clases y guarda los cambios. Hoy hay uno solo, `AppDbContext`.
- **Contexto dios:** un contexto que mapea las tablas de todas las features. Cualquier feature puede leer o escribir cualquier tabla a través de él.
- **Historial de migraciones** (history table): la tabla donde EF anota qué migraciones ya corrió. Hoy es `__EFMigrationsHistory`.
- **Línea base** (baseline): anotar en un historial nuevo que una migración "ya corrió", sin correrla, porque sus tablas ya existen.
- **Schema** (esquema de PostgreSQL): una carpeta de tablas dentro de la base. Hoy todo está en `public`.
- **FK** (foreign key, clave foránea): una restricción de la base que exige que un valor exista en otra tabla. Con `ON DELETE CASCADE`, borrar la fila padre borra las hijas.
- **`TransactionScope`**: una transacción "ambiente" de .NET (`System.Transactions`). Toda conexión que se abre adentro se anota sola en ella.
- **Rolling deploy** (despliegue por tandas): durante un rato conviven la versión vieja y la nueva contra la misma base.

## Conclusión

1. **Recomiendo un contexto por feature más uno de plataforma: `AuthDbContext`, `UsersDbContext`, `TasksDbContext` y `PlatformDbContext` (en `Infrastructure`).** Cada uno en su `Persistence/`, `internal`, con su carpeta de migraciones y su tabla de historial. El ensayo completo pasó: base heredada y base limpia con readiness 200, datos intactos y esquemas iguales (EX-HEREDADA, EX-LIMPIA, EX-DIFF), y 128 de 130 tests en verde, con las 2 fallas esperadas (EX-SUITE, sección 9).
2. **Recomiendo dejar las tablas en `public` en este paso y mover a un schema por feature en un paso propio** (opción C). En este paso se separa solo el historial (`__EFMigrationsHistory_Auth`, `_Users`, `_Tasks`, `_Platform`). Mover las tablas a un schema por feature (`auth`, `users`, `tasks`) es lo que hace la industria (Grzybek, Milan, Wolverine), pero acá cuesta un `RenameTable` que DUNE0018 prohíbe (EX-ESQUEMA) y rompe al release anterior durante un rolling deploy; una vista de compatibilidad no lo salva porque no expone `xmin` (EX-VISTA). **Esto choca con `informe-vsa-deep-modules.md:77`**, que pide un schema por feature. Decide Tobi (sección "Choques con los docs").
3. **La transición sin recrear tablas: línea base en código, sin editar migraciones.** El contexto viejo `AppDbContext` queda sin modelo, solo como dueño de sus 11 migraciones. Al arrancar, si existe `__EFMigrationsHistory`, el inicializador corre las migraciones viejas pendientes, crea cada historial nuevo con `IHistoryRepository.GetCreateIfNotExistsScript()` y anota el `InitialCreate` de cada contexto con `GetInsertScript(new HistoryRow(...))`. En una base vacía, el contexto viejo no corre y cada `InitialCreate` crea sus tablas. Las dos bases quedan iguales (EX-DIFF). EF documenta esta forma de anotar a mano para el caso del squash ([EF, Resetting all migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing#resetting-all-migrations)).
4. **Las tres FKs entre features se borran,** con una migración que genera la CLI en el contexto viejo (`DropForeignKey` x3, EX-SINFK). Una FK no puede cruzar dos contextos sin que uno mapee la tabla del otro. La cascada pasa a código: `DeleteUser` (Users) borra las tareas con un contrato nuevo de Tasks, las claves de idempotencia con un servicio de `Infrastructure`, el perfil y la cuenta, todo en una transacción. No es el evento del paso 24: es una llamada directa por contrato.
5. **El borrado explícito tiene que salir un release antes que el borrado de las FKs** (expand/contract de la FK). EX-VUELTA lo muestra: un binario viejo que corre contra la base sin FKs borra la cuenta y deja 4 tareas huérfanas, con readiness en 200.
6. **La atomicidad entre contextos: `TransactionScope` con `ReadCommitted`, o conexión compartida con `UseTransaction`.** EX-TX probó las dos: las dos son atómicas. `TransactionScope` no necesita plomería y la suite pasó con él (incluida la prueba de dos pedidos simultáneos con la misma clave), pero su commit es síncrono y su aislamiento por defecto es `Serializable`. Recomiendo `TransactionScope` envuelto en un helper de `Infrastructure` que fija `ReadCommitted`. Afecta tres lugares: `RegisterUser`, `DeleteUser` y `IdempotencyFilter` (este último no estaba en la lista de pendientes, sección 6).
7. **La auditoría sigue en el mismo `SaveChanges`:** cada contexto con entidades `IAuditable` mapea `AuditEntries` con `ExcludeFromMigrations()`, que es el caso que documenta EF para compartir una tabla ([EF, Excluding from migrations](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types#excluding-from-migrations)). `PlatformDbContext` es el dueño de la tabla. El mismo `AuditInterceptor` singleton sirve para todos (EX-PARTIDO).
8. **`AppIdentityUser` pasa a `Features/Auth/Persistence/`, `internal`.** Solo lo usan Auth y los tests. Se cierra el pendiente de `progreso.md:98` y la excepción de DUNE0015 (`ContractsAnalyzer.cs:17`).
9. **Dune:** DUNE0004 y DUNE0003 ya frenan que una feature o `Infrastructure` use el contexto de otra feature, y el compilador frena un contexto `public` con entidades `internal` (CS0053) (EX-DUNE). No ven SQL crudo contra una tabla ajena. Recomiendo una regla nueva chica (DUNE0021) para lo que el paso agrega: dónde puede vivir un `DbContext`, uno por feature, y `ExcludeFromMigrations` solo para `AuditEntries` (sección 8).
10. **No hace falta elegir herramienta ni librería.** Todo es EF Core, Npgsql, `System.Transactions` (BCL) y Dune. Las decisiones de diseño para Tobi están al final.

## 1. Varios `DbContext` sobre una sola base en EF Core 10

**Respuesta:** EF lo soporta sin nada extra. Cada contexto tiene su carpeta de migraciones, su snapshot y conviene que tenga su tabla de historial.

- **Migraciones por contexto.** La CLI recibe el contexto y la carpeta: `dotnet ef migrations add InitialCreate --context BlogContext --output-dir Migrations/SqlServerMigrations` ([EF, Using multiple context types](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers#using-multiple-context-types)). Las siguientes van a la misma carpeta sin repetir la opción: "You don't need to specify the output directory for subsequent migrations since they are created as siblings to the last one" (misma página). No encontré un texto que diga literalmente que `--context` es obligatorio; con varios contextos en el ensamblado, la CLI lo pide [EXPERIMENTO EX-PARTIDO: generé las 4 con `--context`].
- **`MigrationsAssembly`** solo hace falta si las migraciones viven en otro proyecto ([EF, Using a separate migrations project](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/projects)). Acá todo vive en `TemplateApp.dll` (ADR-0004 punto 7), así que no se usa.
- **Tabla de historial por contexto.** Se configura con `MigrationsHistoryTable("__MyMigrationsHistory", "mySchema")` ([EF, Custom migrations history table](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table#schema-and-table-name)). EF no la exige por contexto. Milan la recomienda: "Without the `MigrationsHistoryTable` call, both contexts record their migrations in the same default `__EFMigrationsHistory` table, which couples the modules' migration workflows" ([Milan, Schema per module vs database per module](https://milanjovanovic.tech/blog/schema-per-module-vs-database-per-module)). Y avisa que "the default schema doesn't apply to the migrations history table" ([Milan, Multiple DbContext](https://www.milanjovanovic.tech/blog/using-multiple-ef-core-dbcontext-in-single-application)).
- **Qué hace EF con filas del historial que no conoce:** las ignora. `Migrator.PopulateMigrations` recorre solo las migraciones de su ensamblado y busca cada una en el historial; `HistoryRow` tiene solo `MigrationId` y `ProductVersion`, sin `ContextKey` como en EF6 ([Migrator.cs, release/10.0](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/Migrator.cs)). El paso 19 ya lo midió con un binario viejo (`paso-19-expand-contract.md`, conclusión 5). [INFERENCIA] Por eso compartir una tabla de historial funcionaría, pero cada contexto vería las filas de los otros como "aplicadas y desconocidas", y un error de nombres sería silencioso.
- **El bloqueo de `Migrate` no es global en Npgsql.** EF dice que desde EF 9 `Migrate` toma "a database-wide lock" ([EF, Migration locking](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#migration-locking)). Npgsql 10 lo implementa con `LOCK TABLE {historyTable} IN ACCESS EXCLUSIVE MODE`, liberado al final de la transacción ([NpgsqlHistoryRepository.cs, hotfix/10.0.4](https://github.com/npgsql/efcore.pg/blob/hotfix/10.0.4/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs)). [INFERENCIA] Con un historial por contexto, los bloqueos de dos contextos no se excluyen entre sí. El advisory lock propio de `DatabaseInitializer.cs:20` sí cubre todo, así que se queda. Esto responde en parte el pendiente de `progreso.md:194`.
- **EF 10 vuelve a una transacción por migración** (EF 9 las juntaba todas): "This behavior was reverted in EF Core 10" ([EF 9, breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)). Y `Migrate` falla si el modelo tiene cambios sin migración: "an exception is thrown when `dotnet ef database update`, Migrate or MigrateAsync is called" (misma página, *Pending model changes*). Eso importa para el contexto viejo sin modelo (sección 3).
- **Opciones por contexto.** `AddDbContext<TContext>` con `DbContextOptions<TContext>` le da a cada contexto sus opciones aunque haya varios registrados ([EF, DbContext configuration](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)). En EX-PARTIDO, un helper `AddModuleDbContext<TContext>("Tasks")` en `Infrastructure` registra el contexto con la connection string, el historial `__EFMigrationsHistory_Tasks` y el `AuditInterceptor`, y anota el tipo en una lista que leen el arranque y el health check. Cada feature lo llama desde su `<Feature>Feature.cs` (ADR-0004 punto 6).

**Cómo lo hacen los monolitos modulares profesionales:**

| Fuente | Contexto | Datos |
|---|---|---|
| Grzybek, `modular-monolith-with-ddd` | Uno por módulo | "Each Module has it's own data in a separate schema - shared data is not allowed" ([README, 3.1](https://github.com/kgrzybek/modular-monolith-with-ddd)) |
| Milan Jovanović | Uno por módulo, `HasDefaultSchema` | "I always go for logical isolation using schemas"; niveles: tabla, schema, base, otra persistencia ([Modular monolith data isolation](https://www.milanjovanovic.tech/blog/modular-monolith-data-isolation)) |
| Wolverine | Uno por módulo | "separate `DbContext` types for different modules that all target a separate database schema, but still land in the same physical database" ([Wolverine, modular monolith](https://wolverinefx.net/tutorials/modular-monolith.html#with-ef-core)) |
| ABP | Uno por módulo, con prefijo de tabla; une los mapeos para migrar | "EF Core migration system is **not so good in a modular environment**"; unifica los mapeos "to create a unified migration path" ([ABP, EF Core migrations](https://abp.io/docs/latest/framework/data/entity-framework-core/migrations)) |
| eShop | Un contexto por servicio, y una base por servicio | `AddMigration<TContext, TDbSeeder>()` migra y siembra al arrancar ([MigrateDbContextExtensions.cs](https://github.com/dotnet/eShop/blob/main/src/Shared/MigrateDbContextExtensions.cs)) |

[INFERENCIA] El consenso es un contexto por módulo. En los datos hay dos caminos: schema por módulo (Grzybek, Milan, Wolverine) o tablas compartidas con migración unificada (ABP).

## 2. Schema por feature frente a quedarse en `public`

**Respuesta:** para este paso recomiendo `public`, y el schema por feature en un paso propio. El schema es mejor como destino, pero mover tablas que ya existen es un cambio que rompe al release anterior.

**Qué genera EF al mover una tabla de schema.** `MigrationsModelDiffer` empareja la tabla por nombre o por tipo de entidad aunque cambie el schema, y emite `EnsureSchemaOperation` y `RenameTableOperation { NewSchema = ... }` ([MigrationsModelDiffer.cs, release/10.0](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/MigrationsModelDiffer.cs)). Npgsql lo traduce a `ALTER TABLE ... SET SCHEMA` ([NpgsqlMigrationsSqlGenerator.cs, hotfix/10.0.4](https://github.com/npgsql/efcore.pg/blob/hotfix/10.0.4/src/EFCore.PG/Migrations/NpgsqlMigrationsSqlGenerator.cs)). [EXPERIMENTO EX-ESQUEMA] La CLI generó exactamente eso:

```csharp
migrationBuilder.EnsureSchema(name: "tasks");
migrationBuilder.RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks");
```

y el build falló: `error DUNE0018: 'MoveTasksSchema' usa RenameTable` (`MigrationSafetyAnalyzer.cs:19-21`, `conventions.md:115`).

**Por qué rompe al release anterior.** El código viejo nombra `"TaskItems"` sin schema y lo busca en `public`. `SET SCHEMA` toma `ACCESS EXCLUSIVE` ([PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)) y mueve la tabla con sus índices y restricciones (misma página). Después, el release viejo no la encuentra.

- **Una vista de compatibilidad no alcanza.** PostgreSQL deja escribir en una vista simple: "Simple views are automatically updatable" ([PostgreSQL 18, CREATE VIEW](https://www.postgresql.org/docs/18/sql-createview.html#SQL-CREATEVIEW-UPDATABLE-VIEWS)). [EXPERIMENTO EX-VISTA] Con `public."TaskItems"` como vista de `tasks."TaskItems"`, el `SELECT` y el `INSERT ... RETURNING "Id"` anduvieron, pero `SELECT t.xmin` y `RETURNING xmin` fallaron con `column "xmin" does not exist`. `TaskItem.Version` es `xmin` (`TaskItemConfiguration.cs:28-29`, `decisions.tsv`), y EF lo lee en cada consulta y en cada `INSERT`. El release viejo daría error en Tasks.
- **`search_path` sí resuelve el nombre.** [EXPERIMENTO EX-VISTA] Con `ALTER DATABASE ... SET search_path = "$user", public, tasks`, una sesión nueva leyó `"TaskItems"` y su `xmin` desde `tasks`. [INFERENCIA, no medido] Solo vale para sesiones abiertas después del cambio; las conexiones que ya estaban en el pool siguen con el valor viejo. Un camino sin corte sería: release N fija `Search Path` en la connection string con las tablas todavía en `public`; release N+1 las mueve. `ALTER DATABASE` sería esquema escrito a mano, que `conventions.md:92` no admite.

**Qué cuesta cada opción acá:**

| Opción | Gana | Pierde |
|---|---|---|
| A. `public` + historial por contexto, y nada más | Ninguna tabla se mueve: cero riesgo sobre los datos y el release anterior sigue andando (EX-VUELTA: readiness 200 con el binario viejo). Sin choque con DUNE0018. La propiedad se ve en el código (cada contexto mapea solo lo suyo) y Dune la hace cumplir. | La base no muestra de quién es cada tabla. Extraer un módulo (paso 23) pide una lista de tablas en vez de `pg_dump -n <schema>`. No se pueden dar permisos de base por módulo. |
| B. Schema por feature, en este paso | Es lo que hacen Grzybek, Milan y Wolverine. Propiedad visible en la base; permisos y extracción por schema. | `RenameTable` en 12 tablas (las 7 de Identity también). DUNE0018 lo prohíbe: hay que cambiar la regla con ADR o `decisions.tsv`, fixture válida y violación (reglas 4 y 9). Rompe al release anterior; la vista no sirve por `xmin`. Volver atrás exige otro `SET SCHEMA`. |
| **C. `public` ahora, schema en un paso propio (recomendada)** | Cada paso enseña una cosa: el 22 la propiedad en el código, el siguiente el schema con su expand/contract (`search_path` primero, mover después). | Un paso más. Mientras tanto, la base no muestra la propiedad. |

**Qué hace la industria:** schema por módulo desde el principio (Grzybek, Milan, Wolverine). Ninguna de esas fuentes trata la mudanza de tablas que ya existen. [INFERENCIA] Recomiendo C: `public` en este paso y el schema como pendiente, porque el concepto del paso es la propiedad de datos y A la logra sin una migración irreversible.

**Identity en un schema:** si Tobi elige B, ASP.NET Core documenta `modelBuilder.HasDefaultSchema("notdbo")` en el contexto de Identity, con `base.OnModelCreating` primero ([ASP.NET Core, Map to a different schema](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0#map-to-a-different-schema)).

## 3. Pasar de un historial a varios sin recrear tablas

**Respuesta:** línea base en el arranque, con la API pública de EF. No hace falta editar una migración a mano.

**El problema.** El `InitialCreate` que la CLI genera para cada contexto nuevo crea sus tablas. En una base que ya existe, esas tablas están, y `CREATE TABLE` fallaría.

**Opciones:**

| Opción | Qué es | Juicio |
|---|---|---|
| Vaciar el `Up` del `InitialCreate` | Lo que se hacía para adoptar migraciones en una base existente | Es editar una migración a mano: `conventions.md:92` no lo admite. Además, una base limpia no tendría tablas. No encontré esta guía en la documentación actual de EF. |
| `IMigrationsSqlGenerator` propio que agregue `IF NOT EXISTS` | Cambiar cómo EF traduce `CreateTable` | Toca internals de EF para todas las migraciones futuras. Una tabla con otra forma pasaría en silencio. |
| SQL en una migración vieja que inserte las filas del historial nuevo | Una migración del contexto viejo con `migrationBuilder.Sql("INSERT INTO __EFMigrationsHistory_Tasks ...")` | Ata una migración vieja a los ids de las nuevas. Es SQL de esquema a mano. |
| **Línea base en el arranque (recomendada)** | Si existe el historial viejo, el inicializador crea cada historial nuevo y anota su `InitialCreate` como aplicado, sin correrlo | Usa la API pública de EF y no edita migraciones. Es lo que EF documenta para el squash: "Insert a single row into the migrations history, to record that the first migration has already been applied, since your tables are already there" ([EF, Resetting all migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing#resetting-all-migrations)). |

**La API.** `IHistoryRepository` es pública en EF 10 y trae `GetCreateIfNotExistsScript()` ("Generates a SQL script that will create the history table if and only if it does not already exist") y `GetInsertScript(HistoryRow)` ("Generates a SQL script to insert a row into the history table") ([IHistoryRepository, EF 10](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.migrations.ihistoryrepository?view=efcore-10.0)). En Npgsql 10, `ExistsSql` lanza `UnreachableException` ("We should not be checking for the existence of the history table, but rather creating it and catching exceptions", [NpgsqlHistoryRepository.cs](https://github.com/npgsql/efcore.pg/blob/hotfix/10.0.4/src/EFCore.PG/Migrations/Internal/NpgsqlHistoryRepository.cs)); por eso uso `GetCreateIfNotExistsScript` y no `ExistsAsync`. [EXPERIMENTO] El build no dio `EF1001` (API interna) por `HistoryRow` ni `ProductInfo.GetVersion()`.

**El diseño probado en EX-PARTIDO:**

1. El contexto viejo `AppDbContext` queda en `Infrastructure/Persistence`, **sin `DbSet` ni configuración**, con su carpeta `Migrations/` intacta. Sus `.Designer.cs` nombran las entidades como texto, no como tipos, así que compilan sin el modelo. Como su modelo vacío difiere del snapshot, se registra con `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`, la advertencia que EF 9 convirtió en excepción ([EF 9, breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)). Nadie lo usa fuera del arranque.
2. Bajo el advisory lock (`DatabaseInitializer.cs:18-21`), el arranque pregunta `to_regclass('public."__EFMigrationsHistory"')`.
3. **Base heredada** (el historial viejo existe): corre `AppDbContext.MigrateAsync()`, que aplica lo que falte del contexto viejo (por ejemplo `DropCrossFeatureForeignKeys`, sección 5). Después, para cada contexto nuevo, ejecuta `GetCreateIfNotExistsScript()` y, si el historial está vacío, `GetInsertScript(new HistoryRow(<su primera migración>, ProductInfo.GetVersion()))`. La primera migración sale de `Database.GetMigrations().First()`: no hay ids escritos a mano.
4. Para cada contexto nuevo: `MigrateAsync()`. En la base heredada no hay nada pendiente; en una limpia corre cada `InitialCreate`.
5. Siembra (`IDatabaseSeeder`), igual que hoy (`DatabaseInitializer.cs:25-26`).

`ExecuteSqlRaw` está prohibido en el repo (`dune/BannedSymbols.txt:19-22`), así que los dos scripts corren con un `NpgsqlCommand` sobre la conexión del lock. Son SQL que genera EF, no entrada del usuario; el build da `CA2100` (revisar SQL armado) como advertencia [EXPERIMENTO].

**Resultados:**

- **EX-HEREDADA** [EXPERIMENTO]: sobre la semilla de `main`, la API nueva arrancó, aplicó `DropCrossFeatureForeignKeys`, creó los 4 historiales con una fila cada uno (`20261118125332_InitialCreate` en `_Auth`, etc.) y dejó el historial viejo con sus 11 filas. Readiness `200 {"database":"healthy","migrations":"healthy"}`. Las 3 tareas de `ana` siguen (`ana-tres`, `ana-dos-hecha` completada, `ana-uno`), el login con la contraseña de antes anda, crear con clave y repetirla devuelve el mismo id, se registra un usuario nuevo y borrar a `beto` da 204. Quedan solo las FKs de Identity y `FK_Sessions_AspNetUsers_UserId`.
- **EX-LIMPIA** [EXPERIMENTO]: sobre una base vacía no se crea `__EFMigrationsHistory`; se crean las 12 tablas y los 4 historiales. Siembra y `check.mjs` dan lo mismo que EX-CONTROL.
- **EX-DIFF** [EXPERIMENTO]: columnas (68), restricciones (61) e índices (23) de las dos bases son **idénticos**. No comparé el orden de las columnas: en la heredada, `CompletedAt` quedó al final por el paso 19.
- **EX-SUITE** [EXPERIMENTO]: 128 de 130. Falla `TaskCompletionMigrationTests.ExpandBackfill...`: migra solo el contexto viejo con `IMigrator` y salta el arranque, así que los historiales nuevos no existen y readiness da 503. Hay que sumarle la línea base (sección 9). La otra falla es la auditoría del borrado de cuenta (sección 5).

**Riesgos de la línea base:**

- [INFERENCIA] Si un `InitialCreate` nuevo difiere de lo que dejaron las migraciones viejas, la base heredada y la limpia divergen sin error. El guardián es un test permanente tipo EX-DIFF: migra una base por cada camino y compara `information_schema` (sección 9).
- [INFERENCIA] Si alguien agrega una segunda migración a un contexto antes de que una base heredada pase por la línea base, el paso 3 anota solo la primera y `MigrateAsync` corre la segunda: es lo correcto.
- [INFERENCIA] Volver a un binario anterior al paso 22 funciona en una base heredada (EX-VUELTA: readiness 200), pero no en una base que nació con el código nuevo: ahí no hay historial viejo y el binario viejo intentaría crear todo de nuevo.

## 4. Dónde va cada tabla

| Tabla | Contexto | Por qué |
|---|---|---|
| `AspNetUsers`, `AspNetRoles` y las otras 5 de Identity, `Sessions` | `AuthDbContext : IdentityDbContext<AppIdentityUser>` en `Features/Auth/Persistence/` | Identity y la sesión son de Auth (`architecture.md:76`). `Sessions → AspNetUsers` queda dentro del contexto, con su cascada (`SessionConfiguration.cs:23-27`). `AddEntityFrameworkStores<AuthDbContext>()` reemplaza a `AppDbContext` (`AuthFeature.cs:53`). |
| `UserProfiles` | `UsersDbContext` en `Features/Users/Persistence/` | Única tabla de Users. |
| `TaskItems` | `TasksDbContext` en `Features/Tasks/Persistence/` | Única tabla de Tasks. |
| `IdempotencyKeys`, `AuditEntries` | `PlatformDbContext` en `Infrastructure/Persistence/` | Son fontanería del host (`architecture.md:32`). Un contexto propio, y no uno por feature, porque las dos tablas son de todo el host: la clave vale para cualquier `POST` (`conventions.md:80`) y la auditoría para cualquier `IAuditable`. |

- **`AuditEntries` en varios contextos.** La fila de auditoría "viaja en el mismo `SaveChanges`" que el cambio (`architecture.md:57`): `AuditInterceptor` la agrega al contexto que guarda (`AuditInterceptor.cs:60`). Entonces `UsersDbContext` y `TasksDbContext` tienen que mapear `AuditEntry`. EF documenta este caso: "It is sometimes useful to have the same entity type mapped in multiple `DbContext` types", con `ToTable("AspNetUsers", t => t.ExcludeFromMigrations())`, y "migrations will not create the `AspNetUsers` table, but `IdentityUser` is still included in the model" ([EF, Excluding from migrations](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types#excluding-from-migrations)). En EX-PARTIDO, un `modelBuilder.MapAuditEntries()` de `Infrastructure` aplica `AuditEntryConfiguration` y `ExcludeFromMigrations`; los `InitialCreate` de Users y Tasks solo crean su tabla, y el de Platform crea `AuditEntries` e `IdempotencyKeys` [EXPERIMENTO].
- **El interceptor en varios contextos.** "Interceptors are often stateless, which means that a single interceptor instance can be used for all DbContext instances" ([EF, Registering interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#registering-interceptors)). El helper `AddModuleDbContext` lo agrega a cada contexto. En `AuthDbContext` no hace nada: no hay `IAuditable` ahí. La suite de auditoría pasó en EX-PARTIDO salvo el caso de la sección 5.
- **`AppIdentityUser`** pasa a `Features/Auth/Persistence/AppIdentityUser.cs`, `internal`. Lo usan solo Auth, `TestAccounts` y `RegisterUserTests` (grep). No va a `Auth/Domain` porque `architecture.md:31` dice "Domain: sin EF, ASP.NET ni infraestructura" e `IdentityUser` es ASP.NET. Eso cierra el pendiente de `progreso.md:98`, la fila de `architecture.md:45`, la excepción de `conventions.md:17` y la de `ContractsAnalyzer.cs:17` (con su fixture en `ContractsAnalyzerTests.cs:56`).
- **Cada contexto usa `ApplyConfiguration` de sus configuraciones, no `ApplyConfigurationsFromAssembly`.** Hoy `AppDbContext.cs:24` aplica todas las del ensamblado. Con varios contextos, eso metería las tablas de todas las features en cada uno [INFERENCIA, por el mecanismo].

## 5. FKs entre features y la cascada

**Respuesta:** borrarlas, y pasar la cascada a código dentro de `DeleteUser`. Va en dos releases: primero el borrado explícito, después el borrado de las FKs.

**Hoy** hay tres FKs con cascada en `AppDbContext.cs:30-50`: `UserProfiles → AspNetUsers`, `TaskItems → UserProfiles`, `IdempotencyKeys → UserProfiles` (`architecture.md:49-51`). Borrar la cuenta (`DeleteUser.cs:31` → `AuthModule.DeleteAccountAsync`) dispara la cascada en la base. El perfil deja fila `Deleted` en la auditoría porque EF lo tiene rastreado y conoce la relación; las tareas no (`architecture.md:65`).

**Con contextos separados una FK no puede cruzar.** Para que `UsersDbContext` declare `UserProfiles → AspNetUsers`, tendría que mapear `AspNetUsers` (aunque sea con `ExcludeFromMigrations`). Eso es conocer la tabla de Auth, lo que el paso quiere cortar. Y su migración tendría que correr después de la de Auth [INFERENCIA].

**Qué dicen las fuentes:**

- Wolverine: "you probably want to avoid making foreign key relationships in your database between tables owned by different modules" ([Wolverine, modular monolith](https://wolverinefx.net/tutorials/modular-monolith.html#with-ef-core)).
- Grzybek: "Modules don't share data so it is not possible nor desirable to create a transaction which spans more than one module" ([README, 3.7](https://github.com/kgrzybek/modular-monolith-with-ddd)). No encontré un texto suyo sobre FKs en particular.
- Milan las tolera con schemas en la misma base: "If you are using a relational database, you can still maintain referential integrity", y "Removing the foreign keys when extracting tables is not a problem" ([Milan, Data isolation](https://www.milanjovanovic.tech/blog/modular-monolith-data-isolation)).
- Microsoft, entre bases separadas: "you can't create integrity constraints or use distributed transactions between separate databases… you must use eventual consistency" ([Data sovereignty per microservice](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/data-sovereignty-per-microservice)).
- No encontré una fuente primaria con un ejemplo concreto de "borrar el padre en otro módulo". Grzybek remite a Outbox/Inbox y consistencia eventual, que es el paso 24.

**Opciones:**

| Opción | Gana | Pierde |
|---|---|---|
| **A. Borrar las FKs; `DeleteUser` borra en código, por contrato, en una transacción (recomendada)** | Cada contexto es dueño de sus tablas. Las tareas borradas dejan fila de auditoría (hoy no: `architecture.md:65`). No anticipa el paso 24: es una llamada directa, como las que ya existen (`IAuthModule`). | Users pasa a depender de un contrato de Tasks (`ITasksModule.DeleteOwnedTasksAsync`) y de un servicio de `Infrastructure` para las claves. La base ya no impide una tarea huérfana. |
| B. Dejar las FKs solo en SQL (Jovanović) | La base sigue protegiendo la integridad. | Ningún contexto las declara, entonces una base limpia no las tiene, salvo SQL de esquema escrito a mano (`conventions.md:92` no lo admite). La base heredada y la limpia divergen. |
| C. Borrarlas y reaccionar con un evento "cuenta borrada" (Grzybek, Wolverine) | El destino del plan. | Es el paso 24 y el 25: regla 8, no adelantar. |

**Lo que mostró el ensayo:**

- **EX-SINFK** [EXPERIMENTO]: sacar `ConfigureCrossFeatureRelationships` y correr la CLI genera solo tres `DropForeignKey`, y el `Down` las vuelve a crear con `onDelete: Cascade`. DUNE0018 lo deja pasar en cualquier migración (`conventions.md:115`). Sin el borrado explícito fallan 2 tests: `DeleteMe_RemovesStoredIdempotencyKeys` (queda 1 clave) y `AccountDeletion_RecordsTheProfile_ButNotTheCascadedTasks` (falta la fila `Deleted` del perfil).
- **Hueco de cobertura:** `DeleteMe_Cascades_And_AccessBecomes401` (`DeleteUserTests.cs:17-39`) pasa igual. Solo comprueba el 401; **ningún test comprueba que las tareas y el perfil desaparezcan** con la cuenta [EXPERIMENTO EX-SINFK]. Ese test tiene que existir en rojo antes del código (sección 9).
- **EX-PARTIDO** [EXPERIMENTO]: `DeleteUser` abre la transacción, carga el perfil, decide el permiso, llama `ITasksModule.DeleteOwnedTasksAsync` (carga y borra con `SaveChanges`: DUNE0017 prohíbe `ExecuteDelete` sobre `IAuditable`), `IdempotencyKeys.ForgetOwnerAsync` (`ExecuteDelete`: la clave no es auditable), borra el perfil y llama `IAuthModule.DeleteAccountAsync`. Al borrar a `beto` quedaron 0 tareas, 0 claves y 0 sesiones suyas, y la auditoría sumó 3 filas `TaskItem/Deleted` y 1 `UserProfile/Deleted`. `AuditTrailTests.AccountDeletion_..._ButNotTheCascadedTasks` falla porque ahora las tareas **sí** dejan fila: es el cambio de comportamiento buscado, y el test cambia a propósito junto con `architecture.md:65`.
- **EX-VUELTA** [EXPERIMENTO]: el binario de `main` contra la base ya migrada: readiness 200, login, crear tarea y borrar la cuenta de `carla` dan 200/201/204, y quedan **4 tareas huérfanas**. El perfil no quedó huérfano porque EF del binario viejo lo tenía rastreado y todavía conocía la relación.

**Por eso el orden importa (expand/contract de la FK)** [INFERENCIA desde EX-VUELTA y `conventions.md:105`]: el release 1 trae el borrado explícito con las FKs todavía puestas (con FK o sin ella, borra todo); el release 2 borra las FKs y parte los contextos. Si van juntos, un release viejo que todavía atiende durante el despliegue deja huérfanos. Con Compose y una sola instancia pueden ir juntos (`conventions.md:114`), pero el ensayo con binarios lo tiene que mostrar.

## 6. Atomicidad entre contextos

**Respuesta:** hay dos formas que funcionan, y EX-TX las probó. Afectan tres lugares, no dos.

**Dónde se necesita:**

- `RegisterUser` (`RegisterUser.cs:45-63`): crea la identidad, el rol, el perfil (Users, `UsersModule.cs:29-30`) y la sesión en una transacción del `AppDbContext`.
- `DeleteUser`: con la sección 5, borra en Tasks, Platform, Users y Auth.
- **`IdempotencyFilter`** (`IdempotencyFilter.cs:41-80`): abre la transacción en `AppDbContext`, inserta la clave, llama al handler (`CreateTask` guarda con el mismo contexto, `CreateTask.cs:44-45`) y actualiza la clave. "La clave existe si y solo si la tarea existe" depende de que sea el mismo contexto (`conventions.md:81`). Con `PlatformDbContext` y `TasksDbContext` separados, se pierde. **No estaba en la lista de pendientes.**

**Qué documenta EF:** "To share a transaction, the contexts must share both a `DbConnection` and a `DbTransaction`", con `context2.Database.UseTransactionAsync(transaction.GetDbTransaction())` ([EF, Cross-context transaction](https://learn.microsoft.com/en-us/ef/core/saving/transactions#cross-context-transaction)). También documenta `TransactionScope`, con tres límites: depende del proveedor; "Distributed transaction support in System.Transactions was added to .NET 7.0 for Windows only"; y "TransactionScope does not support async commit/rollback; that means that disposing it synchronously blocks the executing thread" ([EF, Limitations of System.Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions#limitations-of-systemtransactions)). Con async hay que pasar `TransactionScopeAsyncFlowOption.Enabled` (misma página).

**Qué documenta Npgsql:** "Npgsql fully supports this API, and automatically enlists if a connection is opened within an ambient TransactionScopes", pero no implementa la recuperación de transacciones distribuidas y "it is discouraged to fully rely on them" ([Npgsql, Basic usage, Transactions](https://www.npgsql.org/doc/basic-usage.html)). Y "PostgreSQL doesn't support nested or concurrent transactions" (misma página).

**EX-TX** [EXPERIMENTO], dos contextos `A` y `B` contra la misma base:

| Caso | Resultado |
|---|---|
| T1. Conexiones separadas; transacción en `A`, `B` guarda; `A` hace rollback | `a=0 b=1`: **`B` quedó guardado.** Es lo que pasaría hoy si solo se parten los contextos. |
| T2. Conexión compartida, `B` sin `UseTransaction` | Una fila: entra en la transacción de `A` (EF no abre transacción para una sola sentencia, [AutoTransactionBehavior.WhenNeeded](https://learn.microsoft.com/en-us/ef/core/saving/transactions#controlling-automatic-transactions)). Dos filas: `InvalidOperationException: A transaction is already in progress; nested/concurrent transactions aren't supported.` |
| T3. Conexión compartida + `B.Database.UseTransactionAsync(...)` | Rollback `a=0 b=0`; commit `a=1 b=1`. Atómico. |
| T4. `TransactionScope` (`ReadCommitted`, async flow), conexiones separadas que EF abre y cierra en orden | Sin `Complete`: `a=0 b=0`. Con `Complete`: `a=1 b=1`. `DistributedIdentifier` vacío (no escaló) y una sola sesión `idle in transaction` en `pg_stat_activity`: las dos conexiones usaron la misma conexión física, una después de la otra. |
| T5. `TransactionScope` con la conexión de `A` abierta a mano mientras `B` guarda | `TransactionAbortedException` al cerrar el scope; nada quedó guardado. Dos conexiones abiertas a la vez no funcionan (en Windows; [INFERENCIA] en Linux tampoco, por el límite de .NET citado arriba). |

**Opciones:**

| Opción | Gana | Pierde |
|---|---|---|
| **A. `TransactionScope` con un helper de `Infrastructure` que fija `ReadCommitted` y `AsyncFlow` (recomendada)** | Sin plomería: cada contexto sigue con su connection string y se anota solo. Los módulos no saben de la transacción del otro (Users no sabe que Auth abrió una). La suite completa pasó con él en EX-PARTIDO, incluida `Create_ConcurrentSameKey_CreatesOneTask_AndBothGetIt` (`CreateTaskTests.cs:108`). | El aislamiento por defecto es `Serializable` ("By default, the transaction executes with isolation level set to Serializable", [System.Transactions, isolation level](https://learn.microsoft.com/en-us/dotnet/framework/data/transactions/implementing-an-implicit-transaction-using-transaction-scope#setting-the-transactionscope-isolation-level)): sin el helper aparecen errores de serialización. El commit bloquea el hilo. Si algo abre dos conexiones a la vez dentro del scope, la transacción aborta (T5): falla segura, no silenciosa. |
| B. Conexión compartida por request + `UseTransaction` (lo que documenta EF; lo que hace ABP) | Async de punta a punta; es el camino documentado por EF. ABP lo automatiza: el segundo contexto con la misma connection string hace `dbContext.Database.UseTransaction(...)` ([UnitOfWorkDbContextProvider.cs](https://github.com/abpframework/abp/blob/dev/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/Uow/EntityFrameworkCore/UnitOfWorkDbContextProvider.cs)). | Hace falta una conexión scoped compartida por todos los contextos y algo que anote cada contexto en la transacción abierta (una unidad de trabajo propia). Sin eso, T2 falla. `HealthCheckConnection.LimitTo` cambia la connection string del contexto (`HealthCheckConnection.cs`), y con una conexión compartida la cambiaría para todos [INFERENCIA]. |
| C. Sin transacción entre módulos: compensar o eventual (Grzybek) | Lo que pide el monolito modular puro. Milan: "Physically yes… But you should avoid it, because a shared transaction couples modules" ([Milan](https://milanjovanovic.tech/blog/schema-per-module-vs-database-per-module)). | Necesita outbox (paso 24) para no perder pasos. Regla 8. |

[INFERENCIA] A y B acoplan los módulos en la transacción; es un acople que el plan acepta hasta el paso 24, y `architecture.md` debería decirlo. Wolverine lo marca como límite: "There is no way to utilize more than one `DbContext` type in a single handler while using the Wolverine transactional middleware" ([Wolverine](https://wolverinefx.net/tutorials/modular-monolith.html#with-ef-core)).

## 7. Arranque, health y tests

**Arranque.** Un solo advisory lock para todo (`DatabaseInitializer.cs:20`), porque el lock de Npgsql es por tabla de historial (sección 1). Orden: contexto viejo (solo en base heredada), línea base, cada contexto nuevo, siembra. eShop hace lo mismo por servicio: `MigrateAsync` y después el seeder ([MigrateDbContextExtensions.cs](https://github.com/dotnet/eShop/blob/main/src/Shared/MigrateDbContextExtensions.cs)). `ConcurrentStartupTests` pasó en EX-SUITE: dos hosts arrancando a la vez sobre una base vacía con cuatro contextos [EXPERIMENTO].

**Health.**

- `PendingMigrationsHealthCheck` (`PendingMigrationsHealthCheck.cs:9-18`) recorre la lista de contextos registrados y suma las pendientes. Excluye el contexto viejo: en una base limpia sus 11 migraciones "faltan" para siempre. EX-HEREDADA y EX-LIMPIA dieron `migrations: healthy` [EXPERIMENTO].
- `DatabaseHealthCheck` (`DatabaseHealthCheck.cs:8`) usa `PlatformDbContext`: así `Infrastructure` no nombra ninguna feature. Esto resuelve el punto 4 de `informe-vsa-deep-modules.md:129`.
- Alternativa: `AddDbContextCheck<TContext>` llama `CanConnectAsync` por contexto ([ASP.NET Core, DbContext probe](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#entity-framework-core-dbcontext-probe)). Pide el paquete `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`, y cuatro checks de conexión contra la misma base no dicen más que uno. No lo recomiendo.
- El body de readiness no cambia (`architecture.md:128`): sigue con `database` y `migrations`.

**Tests** (lo que cambió en EX-SUITE, todo mecánico salvo lo marcado):

- `TestAccounts.SqlAsync`, `LastSeenAsync` e `IdempotencyKeyCountAsync` (`TestAccounts.cs:98-119`) y `AuditLog` (`AuditLog.cs:12` y siguientes) usan SQL crudo: pasan a `PlatformDbContext`. [INFERENCIA] Con SQL crudo, cualquier contexto sirve; elegir el de plataforma evita que el soporte de tests nombre una feature.
- `SetRoleAsync` y `RegisterUserTests` cambian el `using` de `AppIdentityUser`.
- `GetTaskTests`, `ListTasksPlanTests` y `AuditTrailTests.WriteOutsideARequest` pasan a `TasksDbContext`; `PermissionMemoTests` a `AuthDbContext`.
- **No mecánico:** `TaskCompletionMigrationTests` necesita la línea base al final; `AuditTrailTests.AccountDeletion_...` cambia su expectativa.
- Testcontainers no cambia (`PostgresFixture.cs`).

## 8. Dune: ¿hace falta una regla nueva?

**Respuesta:** lo principal ya lo cubren DUNE0004, DUNE0003, DUNE0020 y el compilador. El paso agrega reglas de forma que hoy nadie controla; recomiendo una regla nueva chica.

**Lo que ya frena** [EXPERIMENTO EX-DUNE, violaciones plantadas en EX-PARTIDO]:

- Una clase de Tasks que recibe `UsersDbContext` y lee `UserProfiles`: 8 diagnósticos `DUNE0004 ... 'TemplateApp.Features.Users.Persistence.UsersDbContext' es interior de otra feature` (también por `UserProfile`). `Persistence/` es interior (`conventions.md:19`, `:24`).
- Una clase de `Infrastructure` que recibe `TasksDbContext`: 4 diagnósticos `DUNE0003 ... no está permitido aquí` (`InfrastructureBoundaryAnalyzer.cs:80-91` solo admite `Domain` y `Contracts`).
- Poner `public` a `TasksDbContext`: el compilador falla antes con `CS0053` (el `DbSet<TaskItem>` es menos accesible). DUNE0020 también lo frenaría (`conventions.md:19`).
- Una feature que mapea la entidad de otra (`modelBuilder.Entity<UserProfile>()` en Tasks) cae en DUNE0004 por el tipo [INFERENCIA, por el mismo mecanismo].

**Lo que no frena:**

- **SQL crudo contra la tabla de otra feature.** `_tasks.Database.SqlQuery<int>($"SELECT count(*) ... FROM \"UserProfiles\"")` compiló sin diagnóstico [EXPERIMENTO EX-DUNE]. El texto del SQL no es un tipo. [INFERENCIA] Detectarlo pide leer cadenas y conocer qué tabla es de quién: frágil. Lo propongo como límite documentado, como el de la expresión de colección en `conventions.md:24`.
- **Forma nueva del paso:** dónde vive un `DbContext`, cuántos tiene una feature, y que `ExcludeFromMigrations` sea solo para `AuditEntries`. Sin regla, un segundo contexto en una feature o un `ExcludeFromMigrations` sobre otra tabla compartida pasan.

**Opciones:**

| Opción | Gana | Pierde |
|---|---|---|
| **A. DUNE0021 nueva (recomendada):** (1) un tipo que deriva de `DbContext` vive en `Features/<F>/Persistence/` o `Infrastructure/Persistence/`; (2) a lo sumo uno por feature; (3) `ExcludeFromMigrations` solo dentro de `Infrastructure/Persistence` (el `MapAuditEntries`) | Hace ejecutable la forma que el paso deja en `conventions.md` (regla 4). Mismo estilo que los analyzers de símbolo (`PublicSurfaceAnalyzer`). | Un analyzer más con sus fixtures. |
| B. Solo docs, sin regla | Menos código. | Choca con la regla 4: una regla nueva en `conventions.md` sin Dune. |
| C. Un test de arquitectura sobre el modelo de EF (cada tabla con migraciones en un solo contexto) | Ve la tabla, no el tipo: también una entidad propia mapeada a la tabla ajena con `ToTable("UserProfiles")`. | No es Dune; corre en la suite de backend, que necesita Docker para levantar el host [INFERENCIA]. |

**Rutas fijas que ya funcionan:** `DuneCommentPolicy.cs:12` y `:17` ya exentan `Infrastructure/Persistence/Migrations/` y `Features/<F>/Persistence/Migrations/`, y `DunePaths.IsMigration` mira `/Migrations/` (`DunePaths.cs:18-21`). El build de EX-PARTIDO dio 0 errores con las carpetas nuevas, incluida la subcarpeta `Infrastructure/Persistence/Migrations/Platform/` [EXPERIMENTO]. DUNE0018 revisa las `InitialCreate` nuevas (ids posteriores a `20261117132830`) y las acepta: solo crean tablas e índices sobre tablas que crean.

## 9. Riesgos y qué verificar

**Riesgos:**

1. **Divergencia entre base heredada y limpia** (sección 3). Mitigación: test permanente de comparación de esquema.
2. **Huérfanos durante el despliegue** si el borrado de FKs sale junto con el borrado explícito (EX-VUELTA). Mitigación: dos releases, ensayo con binarios.
3. **Atomicidad perdida en silencio** al partir contextos sin transacción común (EX-TX T1): en el registro, el borrado y la idempotencia.
4. **`TransactionScope` mal configurado:** `Serializable` por defecto, o dos conexiones abiertas a la vez (T5). Mitigación: un solo helper, y que nadie más cree un `TransactionScope`.
5. **El contexto viejo sin modelo** ignora `PendingModelChangesWarning`. Si alguien corre `dotnet ef migrations add --context AppDbContext`, la CLI generaría el borrado de todas las tablas [INFERENCIA, por el mecanismo del snapshot]. DUNE0018 lo frenaría por los `DropTable` fuera de una migración `Contract`.
6. **Volver atrás:** posible en una base heredada (EX-VUELTA), imposible en una base nacida después del paso.
7. **Seguridad del body de readiness:** no cambia.

**Tests en rojo antes del código** (con el código de `main`; los dos primeros pasan hoy y tienen que seguir pasando):

1. `DeleteUser`: después de borrar la cuenta no quedan tareas, perfil, sesiones ni claves de ese usuario. Hoy pasa por la cascada; sin las FKs y sin el borrado explícito, falla (EX-SINFK mostró que el test actual no lo mira).
2. `RegisterUser`: si crear el perfil falla después de crear la identidad, no queda ni la identidad ni el rol. Hoy pasa por la transacción del mismo contexto; partido sin transacción común, falla (T1).
3. `CreateTask` con `Idempotency-Key`: si el handler falla después de que la tarea se guardó, no queda la clave ni la tarea. Mismo motivo.
4. **Ensayo de transición** (sucesor de `TaskCompletionMigrationTests`): una base con el esquema de `main` y datos → el arranque nuevo → datos intactos, un historial por contexto con su `InitialCreate`, readiness 200. Rojo: con el código de `main` no hay historiales nuevos.
5. **Base limpia:** arranque sobre base vacía → readiness 200, sin `__EFMigrationsHistory`.
6. **Esquema igual por los dos caminos** (EX-DIFF como test). Rojo: no existe el camino nuevo.
7. `AuditTrailTests`: las tareas de una cuenta borrada dejan fila `Deleted`. Rojo hoy (la cascada de la base no deja fila).
8. DUNE0021 (si Tobi la elige): fixture válida y violación por cada parte.

**Verificación del plan:** suite completa; `check-all` sin e2e; smoke contra base limpia y contra base migrada; readiness en verde; el ensayo con binarios (`rehearse-migrations.mjs`) con `main` → release 1 → release 2 sobre una base con datos, con el release anterior andando. El contrato HTTP no cambia, así que e2e puede quedar `NOT_RUN` (`AGENTS.md`, Verificación).

## Choques con los docs

Rigen la regla 10 de `AGENTS.md`: no los resuelvo.

1. **Schema por feature.** `informe-vsa-deep-modules.md:77` dice que después del paso 22 hay "Un schema por feature (`HasDefaultSchema`) y una tabla de historial por schema". Mover las tablas existentes genera `RenameTable` (EX-ESQUEMA), que `conventions.md:115` y DUNE0018 hacen error, y rompe al release anterior, contra `conventions.md:105` ("Nunca destructivo en el mismo release"). Las dos cosas no pueden valer a la vez en este paso.
2. **"Suite completa con dos contextos"** (`plan.md:387`). La recomendación deja cuatro contextos nuevos más el viejo sin modelo. [INFERENCIA] El plan parece decir "más de uno"; conviene que Tobi lo confirme.
3. **`architecture.md:37`** dice "misma base y mismo `AppDbContext`" para CQRS; **`architecture.md:57`** dice que el interceptor está "registrado en el `AppDbContext`"; **`architecture.md:65`** dice que las tareas borradas en cascada no dejan fila. Las tres cambian con el paso.
4. **`informe-vsa-deep-modules.md:77`** también pone en Infrastructure "solo un helper genérico tipo `MigrateDbContext<T>`". La recomendación suma `PlatformDbContext` y el contexto viejo en `Infrastructure`. `architecture.md:32` ya pone ahí `IdempotencyKeys` y `AuditEntries`, así que no veo choque con `architecture.md`, pero sí con el informe.
5. **`conventions.md:89`** ("Una operación con varias escrituras atómicas abre una transacción antes de escribir") sigue valiendo, pero la forma cambia de `BeginTransactionAsync` a `TransactionScope`. Hay que escribirlo.

## Decisiones para Tobi

No hace falta elegir herramienta ni librería (regla 5): todo es EF Core, Npgsql, `System.Transactions` (BCL) y Dune.

1. **Tablas: `public` (A), schema por feature ya (B), o `public` ahora y schema en un paso propio (C).** Recomiendo **C**: el paso enseña la propiedad de datos sin una migración irreversible, y el schema queda como pendiente con su propio expand/contract. Choca con `informe-vsa-deep-modules.md:77` (choque 1).
2. **Dónde viven `IdempotencyKeys` y `AuditEntries`: un `PlatformDbContext` en `Infrastructure`, o una copia en cada contexto de feature.** Recomiendo **`PlatformDbContext`**, con `AuditEntries` mapeada en los contextos auditables con `ExcludeFromMigrations`. El nombre `PlatformDbContext` también es una elección.
3. **FKs entre features: borrarlas con borrado explícito (A), dejarlas solo en SQL (B) o evento (C, paso 24).** Recomiendo **A**, en dos releases: primero el borrado explícito, después el borrado de las FKs.
4. **Transacción entre contextos: `TransactionScope` con helper (A) o conexión compartida con `UseTransaction` (B).** Recomiendo **A**, con `ReadCommitted` fijo y un solo helper en `Infrastructure`.
5. **El contexto viejo: dejarlo sin modelo como dueño de las migraciones viejas, o borrarlo y exigir una "parada obligatoria" (no arrancar si la base no está en la última migración vieja).** Recomiendo **dejarlo sin modelo**: no hace falta parada, `TaskCompletionMigrationTests` sigue andando y se puede borrar en un paso futuro de squash.
6. **Dune: DUNE0021 nueva (A), solo docs (B) o test de arquitectura sobre el modelo (C).** Recomiendo **A**, y documentar el SQL crudo como límite.
7. **Auditoría del borrado de cuenta:** con la recomendación 3, las tareas borradas pasan a dejar fila `Deleted`. Recomiendo **aceptarlo** y cambiar `architecture.md:65` y el test.
