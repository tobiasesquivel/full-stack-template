# Paso 22.1: un schema por feature (investigación)

- Fecha: 2026-11-18.
- Estado: **Listo** como investigación. Antes de escribir código, Tobi tiene que decidir los 5 puntos de la sección "Decisiones para Tobi".
- Alcance: el paso 22.1 del [plan](../plan.md) (`plan.md:390-394`). Concepto: aislamiento lógico de los datos; cada módulo tiene su schema (`auth`, `users`, `tasks`, `platform`). Cambia: `HasDefaultSchema`, migraciones con `EnsureSchema` y `RenameTable` de solo schema, connection string, DUNE0018, `informe-vsa-deep-modules.md`. Verifica: ensayo con binarios donde el release anterior sigue andando; base limpia y base migrada con el mismo esquema; readiness en verde.
- Base de esta investigación: el código del paso 22 ya está en `main` (merge `6722055`). Los nombres de este informe son los del código real: `AddMigratedDbContext<T>(schema)` (`ContextRegistration.cs:12`), `CrossContextTransaction`, `SharedAuditEntries`, `MigratedContext`.
- Previa: [paso-22-dbcontext-por-feature.md](paso-22-dbcontext-por-feature.md). Sus secciones 1 y 2 ya midieron `RenameTable`, la vista de compatibilidad y `xmin`. Este informe no las repite; las usa.

**Cómo leer las marcas:**
- `[EXPERIMENTO EX-x]`: lo medí en esta corrida. Los IDs están en la sección "Experimentos".
- `[FUENTE]`: lo leí en un documento primario. Cada uno tiene su link.
- `[CÓDIGO archivo:línea]`: lo leí en el repo.
- `[INFERENCIA]`: deducción mía. No la medí.
- `[NO PROBADO]`: no lo medí. Queda como pendiente.

## Respuesta corta

Sí se puede mover las tablas a un schema por feature sin corte, en dos releases:

1. **Release 22.1a:** la connection string suma `Search Path=public,auth,users,tasks,platform`. No cambia el modelo ni hay migración nueva. `public` tiene que quedar en la lista, porque las tablas siguen ahí.
2. **Release 22.1b:** `HasDefaultSchema` en los cuatro contextos, y una migración `Move*` por contexto que hace `ALTER TABLE ... SET SCHEMA`. Hay que parar el release anterior antes de que arranque este.

Medí las dos cosas con binarios reales. El release anterior con `Search Path` sigue respondiendo (login, lista, creación y borrado de cuenta, sin huérfanos) después de que se mueven las tablas. El mismo release sin `Search Path` falla después del movimiento: login da 500, lista y creación dan 401, y `/health/ready` sigue en 200. Readiness no alcanza para detectar la rotura.

Dos cosas cambian el plan:

- **`HasDefaultSchema` también mueve la tabla compartida `AuditEntries` en las features.** `UsersDbContext` y `TasksDbContext` la mapean sin migrarla. Si no fijan `platform` a mano en `SharedAuditEntries.cs:11`, las escrituras de tareas y de usuarios fallan con `relation "tasks.AuditEntries" does not exist` (EX-MOVE, 6 errores, `POST /tasks` devuelve 500).
- **El `Down` que genera la CLI no mueve la tabla de vuelta.** Genera solo el `DELETE` del historial. Con `newSchema: "public"` escrito a mano, sí genera `ALTER TABLE tasks."TaskItems" SET SCHEMA public;` (EX-DOWN).

## Conclusión

1. **El schema se declara con `HasDefaultSchema`.** Va después de `base.OnModelCreating` en `AuthDbContext` (`AuthDbContext.cs:17`), como muestra la documentación de Identity [FUENTE: Identity, "Map to a different schema"](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0). Con esa forma, EF mueve las 8 tablas de Identity a `auth` (EX-MOVE).
2. **Los historiales ya están en su schema desde el paso 22.** `CREATE SCHEMA auth/platform/tasks/users` y `auth."__EFMigrationsHistory"` aparecen en el log de EX-SQL. Eso no cambia en 22.1.
3. **EF y Npgsql emiten nombres sin schema.** Todo el SQL de la aplicación dice `FROM "TaskItems"` (EX-SQL). Por eso el `Search Path` alcanza para el release anterior.
4. **`public` tiene que quedarse en el path en el release 22.1a.** Sin `public`, la API no arranca (EX-PUBLIC).
5. **Un `Search Path` en código gana a la configuración.** `compose.yaml:66` y el ensayo (`rehearse-migrations.mjs:152`) pisan `ConnectionStrings__DefaultConnection`. Si el path está solo en `appsettings`, se pierde.
6. **Hay que cambiar 4 piezas de SQL a mano**: `IdempotencyFilter.cs:47-49` (funciona con path, conviene calificarlo), `CapturedCommands.cs:15` (se rompe), las consultas raw de los tests con conexión directa (se rompen sin path) y el script `rehearse-migrations.mjs` (se rompe con `psql` sin calificar). Lista completa en la sección 4.
7. **DUNE0018 tiene que dejar pasar un `RenameTable` de solo schema**, pero solo en una migración `Move*` que no hace nada más, y con `newSchema` explícito en el `Down`. Propuesta con fixtures en la sección 5.
8. **Base limpia y base heredada llegan al mismo esquema.** Lo medí (EX-LEG y EX-MOVE). El test de igualdad del paso 22 sigue sirviendo con tres ajustes (sección 6).
9. **La industria separa schema y tablas de forma distinta.** Las fuentes que leí ponen schema por módulo desde el inicio. No encontré una que mueva tablas existentes con release sin corte. Pgroll no mueve tablas: usa vistas y `search_path` (sección 8).
10. **No hace falta elegir herramienta ni librería.** Todo es EF Core, Npgsql, PostgreSQL y Dune. Las decisiones que quedan son de diseño (lista al final).

## Glosario corto

- **Schema** (esquema de PostgreSQL): una carpeta de tablas dentro de la base. Hoy las tablas están en `public`.
- **`search_path`**: la lista de schemas donde PostgreSQL busca un nombre de tabla sin schema. Si el nombre aparece en más de un schema de la lista, gana el primero.
- **`Search Path`** (con mayúsculas, así en la connection string): la clave de Npgsql que pone ese `search_path` en cada conexión nueva. [FUENTE: Npgsql, connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html) dice solo: "Sets the schema search path."
- **`HasDefaultSchema`**: método de EF que pone un schema a todas las tablas que no tienen uno explícito.
- **`SET SCHEMA`**: la orden de PostgreSQL que mueve una tabla a otro schema. EF la genera desde `RenameTable(newSchema: ...)`.
- **Release anterior (R0)**: el binario del paso 22 (`main`, `6722055`). Release 22.1a (R1): el mismo código con `Search Path`. Release 22.1b (R2): el que mueve las tablas.

---

## 1. Cómo se declara el schema de cada contexto

**Respuesta:** `HasDefaultSchema` en cada contexto, después de `base.OnModelCreating` cuando hay base. El historial ya tiene schema. `SET SCHEMA` mueve la tabla con sus índices, restricciones y secuencias.

**Identity (`AuthDbContext`).** La documentación dice que `base.OnModelCreating` va primero y que la configuración de uno sobrescribe la anterior ("last-one-wins") [FUENTE: Identity, customize-identity-model](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0). El ejemplo del repo sigue ese orden: `AuthDbContext.cs:17` llama a `base.OnModelCreating(builder)`, y después va `builder.HasDefaultSchema("auth")` (en el experimento, no en `main`).

- [EXPERIMENTO EX-MOVE] Con ese orden, la CLI generó `EnsureSchema("auth")` más 8 `RenameTable` con `newSchema: "auth"`: `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` y `Sessions`.
- [NO PROBADO] El orden inverso (`HasDefaultSchema` antes de `base`). Por la doc de "last-one-wins", no debería cambiar nada, pero no lo medí.

**Historial de migraciones.** Cada contexto nuevo fija su historial en su schema: `ContextRegistration.cs:18` (`MigrationsHistoryTable(HistoryTable, schema)`). El `AppDbContext` viejo lo deja en `public` (`ContextRegistration.cs:28`). [EXPERIMENTO EX-SQL] Npgsql crea el schema con el historial (`CREATE SCHEMA auth`, etc.). Por eso el paso 22 ya dejó los cuatro schemas creados, y el release 22.1a puede listarlos en el path sin error.

**Qué se mueve con `SET SCHEMA`.** [FUENTE: PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html): "Associated indexes, constraints, and sequences owned by table columns are moved as well." Además, el comando toma `ACCESS EXCLUSIVE` (misma página).

- [EXPERIMENTO EX-SEQ] En una tabla de prueba con `GENERATED BY DEFAULT AS IDENTITY`, la secuencia de identity pasó a `tasks.seqtest_id_seq`. La PK y el índice pasaron a `tasks`. Sobre la base real movida, `platform.AuditEntries_Id_seq` quedó en `platform`.
- [NO PROBADO] `xmin`, la columna de versión de `TaskItem` (`TaskItemConfiguration.cs:28-29`), después del movimiento. `xmin` es una columna del sistema de cada fila, así que no depende del schema [INFERENCIA]. La vista del paso 22 no servía porque no exponía `xmin` (paso-22, EX-VISTA). Antes de cerrar el paso conviene probar un `PUT` con `version` después del `SET SCHEMA`.

**Contextos del paso 22 después de 22.1.**

| Contexto | Schema | Tablas | Cambio en 22.1 |
|---|---|---|---|
| `AuthDbContext` | `auth` | `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`, `Sessions` | `HasDefaultSchema("auth")` después de `base` |
| `UsersDbContext` | `users` | `UserProfiles` | `HasDefaultSchema("users")` |
| `TasksDbContext` | `tasks` | `TaskItems` | `HasDefaultSchema("tasks")` |
| `PlatformDbContext` | `platform` | `IdempotencyKeys`, `AuditEntries` | `HasDefaultSchema("platform")` |

**La tabla compartida `AuditEntries`.** `UsersDbContext` y `TasksDbContext` la mapean con `MapSharedAuditEntries()` (`UsersDbContext.cs:18`, `TasksDbContext.cs:18`). Hoy el mapeo es `Entity<AuditEntry>().ToTable(t => t.ExcludeFromMigrations())` (`SharedAuditEntries.cs:11`). Sin schema explícito, `HasDefaultSchema("tasks")` hace que ese mapeo apunte a `tasks."AuditEntries"`.

- [EXPERIMENTO EX-MOVE] Con el mapeo sin cambios, `POST /tasks` devolvió 500 y el log dijo `relation "tasks.AuditEntries" does not exist` (6 veces). La migración no lo detecta, porque la tabla tiene `ExcludeFromMigrations`.
- Arreglo probado: `ToTable("AuditEntries", "platform", t => t.ExcludeFromMigrations())`. Con ese cambio, todos los pasos del ensayo dieron OK.

**Riesgo para el test de arquitectura.** El test `CapturedCommands` busca el texto `FROM "TaskItems"` (`CapturedCommands.cs:15`). Después de `HasDefaultSchema`, EF genera `FROM tasks."TaskItems"` [EXPERIMENTO EX-SQL en el release con schema]. Usado por `GetTaskTests.cs:74` y `ListTasksPlanTests.cs:34`, el helper dejaría de encontrar las lecturas.

## 2. La secuencia expand/contract sin romper al release anterior

**Respuesta:** tres estados (R0, R1, R2). R1 agrega el path con las tablas todavía en `public`. R2 mueve las tablas. R0 tiene que estar apagado antes de que arranque R2.

| Etapa | Release | Qué cambia | Qué tiene que seguir andando |
|---|---|---|---|
| R0 | paso 22 (ya desplegado) | nada | nada: es el punto de partida |
| R1 | 22.1a (expand) | `Search Path=public,auth,users,tasks,platform` en la connection string. Sin cambio de modelo ni migración | R0 sigue andando (tablas en `public`) |
| R2 | 22.1b (move) | `HasDefaultSchema` en 4 contextos, `Move*` por contexto, auditoría con schema explícito, `Down` con `newSchema` | R1 sigue andando después del movimiento. R0 no: hay que apagarlo antes |
| R3 (opcional) | después de R2 | quitar `public` del path | solo si no queda nada sin calificar en `public` |

**¿EF emite nombres sin calificar cuando no hay schema?** Sí [EXPERIMENTO EX-SQL]. Con `main` contra una base vacía, el log tiene `CREATE TABLE "AspNetUsers"`, `FROM "AspNetRoles"`, `FROM "TaskItems"`, `INSERT INTO "AuditEntries"` y `INSERT INTO "IdempotencyKeys"`, todos sin schema. Los historiales sí salen calificados (`auth."__EFMigrationsHistory"`). Por eso R0 resuelve sus nombres por `search_path`, y R1 lo puede usar para el mismo fin.

**¿Hace falta `public` en el path?** Sí [EXPERIMENTO EX-PUBLIC]. Con `Search Path=auth,users,tasks,platform` (sin `public`) y las tablas todavía en `public`, `main` falla al arrancar: `relation "AspNetRoles" does not exist`. El proceso termina. Mientras haya tablas en `public`, `public` tiene que estar en el path.

**¿Qué pasa con varios `__EFMigrationsHistory` cuando el path los ve todos?** EF no lo nota: el historial se nombra con schema (`auth."__EFMigrationsHistory"`), así que EF no toma el primero del path. El riesgo está en SQL escrito a mano sin calificar:

- [EXPERIMENTO EX-PATH] Con `public."__EFMigrationsHistory"` y `auth."__EFMigrationsHistory"` distintos, `SELECT ... FROM "__EFMigrationsHistory"` sin calificar devolvió la fila de `public` con path `public,auth`, y la de `auth` con path `auth,public`. Con path `tasks,auth` (sin `public`) devolvió la de `auth`.
- El inicializador ya califica el historial viejo: `DatabaseInitializer.cs:49` busca `public."__EFMigrationsHistory"` con `to_regclass`.
- **Hallazgo sobre las bases de prueba:** `ex1` nació con el código del paso 22, así que no tiene `public.__EFMigrationsHistory`. Una base de producción anterior al paso 22 sí lo tiene. Para cubrir ese caso medí la base heredada (EX-LEG): el historial de `public` queda con sus 11 filas.

**¿Qué ve R0 cuando corre R1?** Nada cambia para R0. R1 solo cambia la connection string, y las tablas siguen en `public`. [EXPERIMENTO EX-ROLL] R0 sigue dando login 200, lista 200, creación 201 y readiness 200.

**Cuántos releases:** mínimo dos (R1 y R2). R1 tiene que estar desplegado y el release R0 tiene que estar apagado antes de que arranque R2.

- [EXPERIMENTO EX-NEG] Si R0 sigue vivo cuando R2 mueve las tablas, falla: login 500, lista 401, creación 401, readiness 200. El log tiene `relation "AspNetUsers" does not exist`. Por eso el orden importa, y el ensayo tiene que probarlo con R0 vivo, no solo reiniciado.

**Cómo se vuelve atrás:**

| Desde → hasta | Resultado | Fuente |
|---|---|---|
| R2 → R1 (solo código) | Sigue andando: R1 con `Search Path` resuelve las tablas en su schema | [EXPERIMENTO EX-ROLL] |
| R1 → R0 sin mover las tablas | Sigue andando | [INFERENCIA], mismo caso que R0 con R1 |
| R2 → R0 con las tablas movidas | Falla (ver EX-NEG). Hay que bajar la migración `Move*` antes | [EXPERIMENTO EX-NEG] |
| Bajar la migración `Move*` | El `Down` generado por la CLI no mueve la tabla. Con `newSchema: "public"` escrito a mano sí la mueve | [EXPERIMENTO EX-DOWN]; no lo apliqué sobre una base |

**Ensayo completo medido:**

- [EXPERIMENTO EX-LEG] Base heredada del paso 22: se sembró con el binario anterior al paso 22 (`48dcc7a`), después corrió `main` (el paso 22) y luego R1 con el path, con R0 apagado. R2 migró mientras R1 corría. R1 borró una cuenta con 204 y sin huérfanos (tareas, perfiles, claves y sesiones en 0). Al final, `public.__EFMigrationsHistory` tenía 11 filas, las tablas estaban en sus schemas, y el log de R2 no tenía errores ERR ni FTL.

## 3. Dónde va `Search Path`

**Respuesta:** en código, en un solo helper de `Infrastructure/Persistence` que agrega la clave a la connection string. La configuración no sirve porque se pisa.

| Opción | Gana | Pierde | Juicio |
|---|---|---|---|
| **A. En `appsettings` o en `compose.yaml`** | Visible y simple. | `compose.yaml:66` define `ConnectionStrings__DefaultConnection` como variable de entorno: reemplaza la connection string entera. `rehearse-migrations.mjs:152` hace lo mismo, y `ApiFixture.cs:70` también. El `Search Path` se pierde en silencio. | No. [INFERENCIA de la lectura de esos archivos; la regla de que la variable de entorno gana a `appsettings` es la de la configuración de .NET, no la medí.] |
| **B. En código, un helper que agrega `Search Path` a la connection string antes de usarla** | La agregan todos los caminos: los contextos de DI (`ContextRegistration.cs`), la conexión del lock (`DatabaseInitializer.cs:21`, que usa `legacy.Database.GetConnectionString()`), y el health check (`HealthCheckConnection.cs` reconstruye la cadena desde la misma). Un solo lugar, y se puede testear. | Los tests que abren `NpgsqlConnection` directo con la cadena del fixture no pasan por el helper, salvo que lo llamen. Ver sección 4. | **Recomendada.** |
| **C. `ALTER ROLE` o `ALTER DATABASE ... SET search_path`** | Lo aplica el servidor a todas las sesiones nuevas. | [EXPERIMENTO EX-ALTERDB] Una sesión nueva lee el valor (`SHOW` dio `public, auth`). Pero el estado queda fuera de las migraciones: una base creada de cero (Testcontainers, `TestDatabases.cs:20`) no lo tiene. `conventions.md:95` dice que el esquema no se escribe a mano. | No recomendada. Choca con `conventions.md:95`. |

**Lo que dice cada fuente sobre `search_path`:**

- Npgsql no da una recomendación sobre el valor. Solo define la clave [FUENTE: Npgsql](https://www.npgsql.org/doc/connection-string-parameters.html).
- PostgreSQL: "adding a schema to `search_path` effectively trusts all users having `CREATE` privilege on that schema." Y: "In databases upgraded from PostgreSQL 14 or earlier, everyone has that privilege on the schema `public`." Recomienda `REVOKE CREATE ON SCHEMA public FROM PUBLIC` en algunos casos [FUENTE: PostgreSQL 18, ddl-schemas](https://www.postgresql.org/docs/18/ddl-schemas.html).
- **CVE-2018-1058:** la página de PostgreSQL 18 que leí no menciona el CVE. No lo verifiqué con una fuente de seguridad. Lo que sí dice la página es el riesgo de fondo: un usuario con `CREATE` en un schema del path puede poner funciones que se ejecutan en las consultas de otro.
- Para este repo: la base la usa un solo rol de aplicación (`postgres` en compose). El riesgo es bajo, pero el path tiene que listar solo schemas que controla la app. No poner `"$user"` en el path.

**Recomendación:** opción B, con esta forma:

- Un helper en `Infrastructure/Persistence` que toma `DefaultConnection`, agrega `Search Path=public,auth,users,tasks,platform` con `NpgsqlConnectionStringBuilder` y lo devuelve.
- Lo usan `ContextRegistration` y los tests que abren conexión directa.
- Un test que lea la cadena final y verifique que el path está.
- En R3, si se decide, se saca `public`.

## 4. SQL escrito a mano que nombra tablas sin schema

**Respuesta:** hay 4 lugares de producción o de soporte que se rompen en R2 si el path no está, y varias consultas de tests. Con el path, casi todo sigue andando, salvo `CapturedCommands` y el script del ensayo.

**Producción y soporte:**

| Archivo:línea | SQL | ¿Se rompe en R2 sin path? | ¿Y con path? | Recomendación |
|---|---|---|---|---|
| `backend/Infrastructure/Http/IdempotencyFilter.cs:47-49` | `INSERT INTO "IdempotencyKeys" ...` con `ExecuteSqlAsync` | Sí | Sí [EXPERIMENTO EX-ROLL: el `INSERT` se ejecutó en R2] | Calificar: `platform."IdempotencyKeys"` en R2 |
| `backend/Infrastructure/Persistence/DatabaseInitializer.cs:49` | `to_regclass('public."__EFMigrationsHistory"')` | No (ya califica) | Sí | Mantener |
| `backend/Infrastructure/Persistence/DatabaseHealthCheck.cs:17` | `SELECT 1` | No | Sí | Ninguna |

**Tests que nombran tablas sin schema:**

| Archivo:línea | Qué hace | ¿Se rompe en R2? | Recomendación |
|---|---|---|---|
| `backend/Tests/Support/CapturedCommands.cs:15` | Busca `FROM "{table}"` en el texto capturado | **Sí**, con `HasDefaultSchema` el texto es `FROM tasks."TaskItems"` [EXPERIMENTO EX-SQL] | Cambiar el match a un patrón que acepte schema: `"TaskItems"` sin prefijo fijo |
| `backend/Features/Tasks/GetTask/GetTaskTests.cs:74` y `backend/Features/Tasks/ListTasks/ListTasksPlanTests.cs:34` | Usan `CapturedCommands` | Sí, por lo de arriba | Mismo cambio |
| `backend/Tests/Persistence/TaskCompletionMigrationTests.cs:33-148` | `NpgsqlCommand` directo con la cadena de `TestDatabases` | **Sí**: la conexión directa no tiene path | Usar el helper de la sección 3 o calificar |
| `backend/Tests/Persistence/PerFeatureContextsTransitionTests.cs:35-47` | `INSERT` sin schema con `NpgsqlCommand` directo | **Sí**, mismo motivo | Igual |
| `backend/Tests/Persistence/PerFeatureContextsTransitionTests.cs:118` y `:128` | `to_regclass('{table}')` y `FROM "{table}"` | **Sí** | Igual |
| `backend/Tests/Persistence/PerFeatureContextsTransitionTests.cs:142`, `:145`, `:151` | `c.conrelid::regclass::text` | Sí, y además es path-dependiente (ver sección 6) | Usar `nspname || '.' || relname` |
| `backend/Tests/Startup/ConcurrentStartupTests.cs:87` | `DELETE FROM "AspNetRoleClaims"` con conexión directa (`:38`) | **Sí** | Helper o calificar |
| `backend/Features/Tasks/ListTasks/ListTasksPlanTests.cs:91-117` | Seed con `NpgsqlCommand` directo | **Sí** | Helper o calificar |
| `backend/Tests/Support/TestAccounts.cs:98-110`, `DatabaseRows.cs:13-23`, `AuditLog.cs:14-45` | SQL vía EF (`ExecuteSqlAsync`, `SqlQuery`) | No, si el contexto de EF tiene el path | Ninguna, con B |
| `backend/Features/Auth/RegisterUser/RegisterUserTests.cs:90-91`, `backend/Features/Auth/CloseSession/CloseSessionTests.cs:53-91` | SQL vía EF | No | Ninguna |

**Script del ensayo** (`.agents/skills/verify-template/scripts/rehearse-migrations.mjs`):

- `:63-65` la función `psql()` usa `docker exec ... psql`. Una sesión de `psql` tiene el `search_path` por defecto (`"$user", public`). [EXPERIMENTO EX-PSQL] Sobre la base movida, `SELECT count(*) FROM "TaskItems"` dio `relation "TaskItems" does not exist`.
- `:72-79` (`orphans()`) y `:276-282` (chequeo de cuenta borrada) nombran `"TaskItems"`, `"UserProfiles"`, `"IdempotencyKeys"`, `"Sessions"` y `"AspNetUsers"` sin schema. **Se rompen en R2.** Con `psql` sin `SET search_path`, el chequeo sería el que falla, no la app.
- `:312-322` (siembra masiva) usa `information_schema.columns WHERE table_name = 'TaskItems'` sin filtrar schema, y luego `INSERT INTO "TaskItems"`. **Se rompe.**
- `:364` (`finalSchema`) filtra solo por nombre de tabla. Funciona, pero conviene filtrar por schema.
- `:68-69` (`migrationHistory()`) ya recorre los schemas con `information_schema.tables` y califica. **No se rompe.**
- `:152`: el ensayo pone la connection string por variable de entorno. Por eso el path tiene que venir del código (opción B). Si viene de `appsettings`, el ensayo lo pierde.

**Dune.** No encontré SQL de producción que nombre tablas en `dune/`. Las menciones que hay son fixtures de analyzers en tests [lectura parcial, no exhaustiva].

**Recomendación:** calificar el SQL que escribe la app (`IdempotencyFilter`) y el del ensayo. Así no depende del path y el chequeo no se mezcla con el mecanismo que prueba. Los tests de conexión directa usan el helper de la sección 3.

## 5. DUNE0018 y `RenameTable`

**Respuesta:** hoy el analyzer marca cualquier `RenameTable`. La regla nueva tiene que dejar pasar solo el cambio de schema con el mismo nombre, en una migración que no hace otra cosa, con el `Down` explícito.

**Cómo funciona hoy.** `MigrationSafetyAnalyzer.cs:19-22` pone `RenameColumn` y `RenameTable` en el conjunto `Renames`. Luego `:105-108` reporta siempre `MigrationRename`. El analyzer no mira `newSchema`.

**Dos cosas que medí con el analyzer activo:**

- [EXPERIMENTO EX-DUNE] Las cuatro migraciones `Move*` generan 12 `RenameTable`. El build dio **24** líneas `error DUNE0018`. Cada operación sale dos veces. No investigué la causa: `OperationBlocks` se recorre dos veces para el mismo `Up`, o algo parecido. Hay que mirarlo antes de cambiar el analyzer, porque los tests cuentan diagnósticos (`MigrationSafetyAnalyzerTests.cs:194`).
- [EXPERIMENTO EX-DOWN] El `Down` que genera la CLI para `MoveTasksToSchema` es `RenameTable(name, schema: "tasks", newName)` sin `newSchema`. Npgsql no emite nada: solo queda el `DELETE` del historial. Con `newSchema: "public"`, emite `ALTER TABLE tasks."TaskItems" SET SCHEMA public;`. El analyzer no revisa el `Down` hoy.

**Opciones:**

| Opción | Gana | Pierde | Juicio |
|---|---|---|---|
| **A. Permitir el cambio de schema dentro de `Contract*`** | Usa un prefijo que ya existe. | `Contract*` hoy solo admite borrados (`conventions.md:118`, `MigrationContractMixed`). Hay que agregar excepciones al control de mezcla. Un `Contract` que también borra algo queda más difícil de leer. | No recomendada. |
| **B. Prefijo nuevo `Move*`, con solo `EnsureSchema` y `RenameTable` de schema, y `Down` con `newSchema`** | `Contract*` sigue siendo solo borrados. Cada migración hace una cosa. El `Down` no puede quedar en no-op en silencio. | Un prefijo nuevo en `conventions.md`. Hay que documentarlo. | **Recomendada.** |
| **C. Sin regla nueva: `RenameTable` queda prohibido y la mudanza se hace a mano con SQL** | Sin cambio en Dune. | Choca con `conventions.md:95` (el esquema no se escribe a mano) y con la regla 4 del repo (regla nueva, regla ejecutable). | No. |

**Regla propuesta (opción B):**

- Un `RenameTable` pasa solo si `name == newName`, `newSchema` no es nulo y es distinto del schema de origen.
- Solo puede estar en una migración cuyo nombre empieza con `Move`.
- La migración `Move*` solo puede tener `EnsureSchema` y `RenameTable` de ese tipo.
- En el `Down` de esa migración, `RenameTable` tiene que traer `newSchema` no nulo.
- Lo que el analyzer no puede ver: que R1 ya esté desplegado. Eso es un paso de proceso y lo prueba el ensayo (sección 7).

**Fixtures (para la regla 9: una válida y una violación por parte).** No las ejecuté en Dune. Es la forma propuesta, con los nombres de los tests que ya existen como modelo (`MigrationSafetyAnalyzerTests.cs:121-210`):

| Caso | Migración | Resultado esperado |
|---|---|---|
| Válida | `20261118200000_MoveTasksToSchema`: `EnsureSchema("tasks")` y `RenameTable(name: "TaskItems", newName: "TaskItems", newSchema: "tasks")`. `Down` con `newSchema: "public"` | 0 DUNE0018 |
| Violación: rename | `Move*` con `RenameTable(name: "TaskItems", newName: "Tasks")` (el caso que ya existe en `:151`) | DUNE0018 |
| Violación: mezcla | `Move*` con `EnsureSchema` y además `AddColumn` | DUNE0018 por el `AddColumn` |
| Violación: fuera de `Move` | `Contract*` con `RenameTable` de solo schema | DUNE0018 |
| Violación: `Down` sin destino | `Move*` cuyo `Down` tiene `RenameTable` sin `newSchema` | DUNE0018 |

**Tampoco toca `DropSchema`:** la lista `Drops` (`MigrationSafetyAnalyzer.cs:24-30`) ya lo incluye, así que un `Down` con `DropSchema` sigue prohibido fuera de `Contract`.

**Lo que hay que cambiar en docs (regla 10):** `conventions.md:118` dice hoy que `RenameTable` es error. Hay que cambiar esa línea junto con el analyzer y dejar `decisions.tsv` con la fila correspondiente.

## 6. Base limpia frente a base migrada

**Respuesta:** sí llegan al mismo esquema. El test de igualdad del paso 22 sigue sirviendo, con tres ajustes.

**Por qué convergen.** En base vacía corre `InitialCreate` (crea las tablas en `public`) y después `Move*` las mueve. En base heredada, la línea base marca `InitialCreate` como aplicado (`DatabaseInitializer.cs:27-31`), y `Move*` mueve las tablas que ya existen. El resultado final es el mismo. [EXPERIMENTO EX-LEG y EX-MOVE] La base heredada terminó con las tablas en sus schemas y el historial de `public` intacto.

**Los tres ajustes al test (`PerFeatureContextsTransitionTests.cs`):**

1. **Sus consultas sin schema.** Las inserciones (`:35-47`) y `to_regclass` (`:118`) usan nombres sin calificar sobre una conexión directa. Sin path, fallan. Usar el helper de la sección 3 o calificar.
2. **`regclass::text` depende del path.** `c.conrelid::regclass::text` (`:142`, `:145`, `:151`) muestra el nombre sin schema cuando el schema está en el path, y con schema cuando no. Dos conexiones con path distinto dan textos distintos. Hay que reemplazarlo por `nspname || '.' || relname`. [INFERENCIA: lo deduzco de la documentación de `search_path` y de cómo PostgreSQL formatea `regclass`; no lo medí.]
3. **Agregar una lista explícita de schema por tabla.** Hoy el test compara columnas, restricciones e índices, pero no afirma en qué schema debe estar cada tabla. Si las dos bases dejaran todo en `public`, el test seguiría verde. Agregar una comparación contra una lista fija (`auth."AspNetUsers"`, `users."UserProfiles"`, etc.).

## 7. El ensayo con binarios (`rehearse-migrations.mjs`)

**Respuesta:** el script ya hace casi todo lo que hace falta. Hay que agregar una etapa de control negativo, calificar sus consultas y poner el path en código.

**Etapas propuestas** (las refs de git son commits del mismo branch):

| Etapa | Refs | Qué se comprueba |
|---|---|---|
| 0 | `main` (R0) siembra datos | Readiness y lecturas. Igual que hoy |
| 1 | R0 → R1 | R1 arranca con R0 vivo. Escrituras cruzadas. Lecturas por ambos. R0 borra una cuenta: 204 y 0 huérfanos |
| 2 | R1 → R2 | R2 migra con R1 vivo. Las mismas comprobaciones. R1 borra una cuenta: 204 y 0 huérfanos |
| Control negativo | R0 → R2 | **Tiene que fallar**: R0 con las tablas movidas. Sirve para probar que el ensayo detecta la rotura, porque readiness sigue en 200 |

**Qué cambia en el script:**

- `psql()` (`:63-65`): calificar `orphans()` (`:72-79`), el chequeo de cuenta (`:276-282`), la siembra (`:312-322`) y `finalSchema` (`:364`). La opción de poner `SET search_path` en la sesión de `psql` oculta el problema que se quiere probar; calificar es más honesto.
- Connection string: el path viene de la app (opción B), no del `ConnectionStrings__DefaultConnection` del script. Si no, el ensayo no prueba lo que corre en compose.
- Control negativo: una etapa adicional que espera fallo. Hoy el script no tiene esa forma.
- Readiness no basta. Ya lo dice `conventions.md:119` ("Readiness solo dice que no faltan migraciones"), y EX-NEG lo confirmó. El plan dice "readiness en verde en cada etapa" (`plan.md:394`): hay que cambiarlo a "readiness y lecturas y escrituras y borrado sin huérfanos".

**Lo que midió el ensayo en esta corrida.** No corrí el script. Hice a mano la misma secuencia con los mismos binarios y las mismas comprobaciones (EX-ROLL, EX-LEG, EX-NEG). Eso no reemplaza el script. Falta correrlo con las refs reales antes de cerrar el paso.

## 8. Qué hace la industria

**Respuesta:** las fuentes que leí no mueven tablas existentes sin corte. La mayoría parte de schema por módulo desde el principio.

- **Pgroll (Neon).** No mueve tablas. Cada versión del esquema es un schema con vistas que apuntan a las tablas de `public`. Las aplicaciones eligen versión con el parámetro `search_path` de su conexión [FUENTE: Neon, pgroll](https://neon.com/guides/pgroll). Es el mismo principio del `Search Path` de R1, pero sin mover datos.
- **PostgreSQL (lista pgsql-admin).** Un hilo sobre mover tablas a otro schema con poca caída dice que `ALTER TABLE` queda bloqueado por transacciones concurrentes que usen la tabla, y que esas transacciones quedan bloqueadas a su vez. La recomendación: no tener transacciones largas sobre esa tabla y hacer `COMMIT` enseguida después del `ALTER` [FUENTE: pgsql-admin, Laurenz Albe](https://www.postgresql.org/message-id/efa851fec1b04047ef4af045831b628834c00a8a.camel@cybertec.at). Eso confirma el `ACCESS EXCLUSIVE` de la documentación de PostgreSQL.
- **Milan Jovanović, Kamil Grzybek, Wolverine, ABP.** Ya están citados en [paso-22](paso-22-dbcontext-por-feature.md), sección 1 y 2. No los volví a leer en esta corrida. Ninguno de los que revisé en el paso 22 trata la mudanza de tablas existentes con release sin corte.

**[INFERENCIA] Riesgo de bloqueo en producción.** El `SET SCHEMA` de R2 necesita `ACCESS EXCLUSIVE` sobre cada tabla. Si hay una transacción larga sobre `TaskItems`, la migración espera y bloquea a las siguientes. Mitigación posible: `lock_timeout` antes del movimiento, con un reintento. No probé esa mitigación.

## Choques con los docs

Rigen la regla 10 de `AGENTS.md` (`AGENTS.md:46`): no los resuelvo. Los dejo para Tobi.

1. **`RenameTable` de solo schema.** `plan.md:393` dice que 22.1 cambia DUNE0018 para permitirlo. `conventions.md:118` dice hoy que `RenameTable` es error. Son dos docs que no coinciden hasta que se cambie la regla. Hay que resolverlo en el mismo paso, con la regla 9 (fila en `decisions.tsv`, fixture válida y violación).
2. **`conventions.md:95` ("el esquema no se escribe a mano").** Si se elige la opción C (`ALTER DATABASE`) de la sección 3, choca con esta línea. Con la opción B no hay choque.
3. **`informe-vsa-deep-modules.md:77`** describe el estado final después del paso 22 (`HasDefaultSchema` y historial por schema). Coincide con el estado de 22.1 cuando termine. No hay choque, pero el informe describe 22.1 como si ya estuviera hecho, así que hay que leerlo como estado final.
4. **`architecture.md:51`** dice que las tablas siguen en `public` hasta 22.1. Se vuelve falso al terminar 22.1: hay que actualizarlo con el paso.
5. **`plan.md:394`** ("readiness en verde en cada etapa") no alcanza para detectar la rotura, como muestra EX-NEG. `conventions.md:119` ya lo dice. El plan tiene que reflejarlo.
6. **Pendiente de otro paso, no de este:** `plan.md:387` dice "suite completa con dos contextos", y el paso 22 dejó cuatro. Ya está en `paso-22`, sección "Choques".

## Experimentos

Todos corrieron fuera del checkout del paso, en un `git worktree` en modo `--detach` (sin rama) desde `HEAD` (`6722055`), en el scratchpad de esta sesión, con PostgreSQL 18 en Docker (`postgres:18`, puerto 55422, `log_statement=all`). Binarios: `dotnet publish` de `backend/TemplateApp.csproj` en Release. Herramienta de migraciones: `dotnet-ef` 10.0.12 instalado en el scratchpad. El driver HTTP `drive.mjs` hace el mismo recorrido que `rehearse-migrations.mjs` (registro, login, lista, creación con `Idempotency-Key`, PUT con `version`, borrado de cuenta).

| ID | Qué hice | Resultado | Tipo |
|---|---|---|---|
| EX-SQL | `main` contra base vacía (`ex1`), siembra por HTTP, log completo | Tablas, lecturas y escrituras sin schema. Historiales calificados. `CREATE SCHEMA` de los cuatro schemas | EXPERIMENTO |
| EX-PUBLIC | `main` con `Search Path` sin `public`, tablas en `public` (`ex5`) | No arranca: `relation "AspNetRoles" does not exist` | EXPERIMENTO |
| EX-ROLL | R0 con path (`ex2`), luego R1 con path migrando (`bin-move-*`), R0 vivo | R0 y R1 dan login 200, lista 200, creación 201, readiness 200 antes y después del movimiento | EXPERIMENTO |
| EX-MOVE | Release con `HasDefaultSchema` y 4 migraciones `Move*` generadas con `dotnet ef` | 12 `ALTER TABLE ... SET SCHEMA` en el log. Con el mapeo compartido sin schema: `POST /tasks` 500, `relation "tasks.AuditEntries" does not exist` (6). Con el arreglo de `platform`: todo OK | EXPERIMENTO |
| EX-NEG | `main` sin path vivo; después R2 migra | Login 500, lista 401, creación 401, readiness 200. Log: `relation "AspNetUsers" does not exist` | EXPERIMENTO |
| EX-LEG | Base del binario pre-paso-22 (`48dcc7a`), luego `main`, luego R1 y R2 con R0 apagado antes de R2 | Todo 200/201/204. Borrado con 0 huérfanos. `public.__EFMigrationsHistory` con 11 filas. Log de R2 sin ERR ni FTL | EXPERIMENTO |
| EX-SEQ | SQL en `ex3`: `SET SCHEMA` sobre tabla con identity, índice y PK. Base real `ex7`: secuencias | Secuencia, índice y PK pasan al nuevo schema. `platform.AuditEntries_Id_seq` | EXPERIMENTO |
| EX-PATH | SQL en `ex3`: `CREATE` sin schema y `SELECT` sin calificar con varios schemas | `CREATE` va al primer schema del path que existe. El `SELECT` toma el primero. Sin `public` en el path, `relation does not exist` | EXPERIMENTO |
| EX-ALTERDB | `ALTER DATABASE ex3 SET search_path = public, auth` y sesión nueva | La sesión nueva lee `public, auth`. No medí el efecto en sesiones ya abiertas | EXPERIMENTO (parcial) |
| EX-PSQL | `SELECT count(*) FROM "TaskItems"` en `psql` sobre la base movida | `relation "TaskItems" does not exist` | EXPERIMENTO |
| EX-DOWN | `dotnet ef migrations script` de `MoveTasksToSchema` a `InitialCreate` | Sin `newSchema`: solo el `DELETE` del historial. Con `newSchema: "public"`: `ALTER TABLE tasks."TaskItems" SET SCHEMA public;`. No lo apliqué sobre una base | EXPERIMENTO (generado, no ejecutado) |
| EX-DUNE | Build con `dune/Dune.props` activo en la copia | 12 `RenameTable` dan 24 líneas `error DUNE0018` | EXPERIMENTO |
| EX-SQL-CAP | El texto de lectura después de `HasDefaultSchema` (log de R2) | `FROM tasks."TaskItems"`, no `FROM "TaskItems"`. Rompe el match de `CapturedCommands` | EXPERIMENTO (texto del log) + lectura del helper |

**Qué no probé (queda pendiente):**

- [NO PROBADO] `HasDefaultSchema` antes de `base.OnModelCreating` (solo probé el orden de la doc).
- [NO PROBADO] `xmin` y un `PUT` con `version` después del movimiento.
- [NO PROBADO] Aplicar el `Down` completo sobre una base y volver a R0.
- [NO PROBADO] Correr `dotnet test` o `rehearse-migrations.mjs` completos. Los tests que cambian (`CapturedCommands`, los raw de los tests) no los corrí.
- [NO PROBADO] `lock_timeout` como mitigación del bloqueo de `SET SCHEMA`.
- [NO PROBADO] El efecto de `ALTER DATABASE` sobre sesiones ya abiertas.
- [NO PROBADO] Que las cuatro migraciones `Move*` pasen las reglas de Dune. Dune estaba desactivado en el build de los binarios.

## Decisiones para Tobi

No hace falta elegir herramienta ni librería (regla 5): todo es EF Core, Npgsql, PostgreSQL y Dune.

1. **Dónde va `Search Path`.** A (configuración: no, se pierde en compose y en el ensayo), B (código, un helper; recomendada), C (`ALTER DATABASE`: choca con `conventions.md:95`). Recomiendo **B**: es el único lugar que sobrevive a `compose.yaml:66`, al ensayo y a `ApiFixture`.
2. **`public` en el path después de R2.** Mantenerlo (recomendada): el historial legado queda en `public` y no molesta. O sacarlo en R3: queda más limpio, pero si algo sin calificar se crea en `public` por error, ya no hay red. Recomiendo **mantenerlo** hasta que el historial legado se pueda borrar.
3. **Forma de la regla DUNE0018.** A (dentro de `Contract*`, con excepciones) o B (prefijo `Move*`, con solo `EnsureSchema` y `RenameTable` de schema, y `Down` con `newSchema`). Recomiendo **B**: `Contract*` sigue siendo solo borrados, y cada migración hace una sola cosa.
4. **Cómo se entrega el paso.** Un PR con dos commits (commit de R1 y commit de R2, en ese orden), o dos PRs bajo el paso 22.1. Recomiendo **un PR con dos commits**: la regla 6 pide un PR por paso, y el ensayo ya toma commits como refs. R1 se despliega solo, y R2 después.
5. **SQL escrito a mano.** Calificar `IdempotencyFilter` y el script del ensayo (recomendada), o dejarlos al `search_path`. Recomiendo **calificar**: el chequeo no debe depender del mecanismo que prueba.

## Cierre del informe

- Git: el checkout del paso solo suma este archivo. El experimento corrió en un worktree aparte que se borró al final.
- Pendiente que este informe recibe: `progreso.md:76` (22.1 en estado pendiente).

## Fuentes

- [Npgsql, connection string parameters (Search Path)](https://www.npgsql.org/doc/connection-string-parameters.html)
- [PostgreSQL 18, Schemas y search_path](https://www.postgresql.org/docs/18/ddl-schemas.html)
- [PostgreSQL 18, ALTER TABLE (SET SCHEMA)](https://www.postgresql.org/docs/18/sql-altertable.html)
- [ASP.NET Core 10, Identity model customization (base primero, HasDefaultSchema)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0)
- [EF Core, Custom migrations history table (MigrationsHistoryTable con schema)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table)
- [Neon, pgroll (vistas y search_path por versión)](https://neon.com/guides/pgroll)
- [pgsql-admin, Laurenz Albe, ALTER TABLE bloqueado por transacciones concurrentes](https://www.postgresql.org/message-id/efa851fec1b04047ef4af045831b628834c00a8a.camel@cybertec.at)
- Citadas en [paso-22](paso-22-dbcontext-por-feature.md) y no re-verificadas aquí: Grzybek, Milan Jovanović, Wolverine, ABP.
- No encontré la página de EF sobre `HasDefaultSchema` (respondió 404). El comportamiento lo medí en los experimentos.
