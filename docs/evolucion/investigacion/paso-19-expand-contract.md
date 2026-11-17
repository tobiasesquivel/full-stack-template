# Paso 19: migraciones expand/contract ensayadas (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 19 del [plan](../plan.md) (`plan.md:360-363`). Concepto: despliegue sin corte; nunca destructivo en el mismo release. Cambia: migraciones, `docs/conventions.md`, evidencia de la secuencia completa. Verifica: ciclo probado sobre una base con datos (agregar, backfill, cambiar código, borrar), con readiness en verde en cada etapa.
- Cómo está hoy:
  - Seis migraciones en `backend/Infrastructure/Persistence/Migrations/`, desde `InitialCreate` hasta `AddTaskListIndex`. Las genera la CLI (`docs/conventions.md:91`).
  - La API migra al arrancar. `DatabaseInitializer.cs:20` toma `pg_advisory_lock` y `DatabaseInitializer.cs:24` llama `db.Database.MigrateAsync`.
  - Readiness corre `PendingMigrationsHealthCheck` (`backend/Infrastructure/Persistence/PendingMigrationsHealthCheck.cs:18`), que cuenta las migraciones del ensamblado que la base no tiene.
  - `TaskItem.IsCompleted` es un `bool` (`backend/Features/Tasks/Domain/TaskItem.cs:12`). `TaskResponse` lo expone como `isCompleted` (`backend/Features/Tasks/TaskResponse.cs:3`, `TaskResponses.cs:9`).
  - `UpdateTask` calcula cuándo se completa una tarea (`completes`, `backend/Features/Tasks/UpdateTask/UpdateTask.cs:47`). Ese momento solo sale como métrica (`UpdateTask.cs:64`, `backend/Features/Tasks/TaskMetrics.cs:31`). No se guarda en ninguna columna.
  - `docs/architecture.md:65` dice que las migraciones "son forward: renombran y hacen backfill, sin reset de base ni `EnsureCreated`".
  - El paso 18 dejó pendiente: `AddTaskListIndex` borra `IX_TaskItems_OwnerId` y crea el compuesto en la misma transacción, sin `CONCURRENTLY` (`docs/evolucion/progreso.md`, Pendientes; `docs/decisions.tsv:218`).
- Decisiones ya tomadas (este informe no las reabre): EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:36`); PostgreSQL 18 en tests; xUnit v3 con Testcontainers; `xmin` como versión (`docs/decisions.tsv:144`); migraciones forward generadas por CLI; analyzers Roslyn y gates Node en `dune/`.
- Método: solo fuentes primarias. Son la documentación de Fowler, GitLab, Rails (strong_migrations, MIT), PostgreSQL 18, Microsoft Learn, Npgsql, el código de `dotnet/efcore` en `release/10.0` y los repos de las herramientas. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA]. Lo que salió de una corrida mía va marcado [EXPERIMENTO].
- Evidencia propia (todo fuera del repo; el repo quedó sin cambios, `git status` vacío):
  - **EX-EF:** `dotnet ef` sobre copias de `backend/` sin `Tests/`, en `C:\Users\Tobi\AppData\Local\Temp\claude\bac9aed0421ea2d1\316995a0-2128-4720-bc37-27d66839b553\scratchpad\ef-exp` y `...\scratchpad\ef-exp2`. Ocho casos de cambio de modelo (sección 2). EF 10.0.2 de herramienta, 10.0.12 de runtime (avisa, pero genera).
  - **EX-DB:** un `postgres:18` (18.6) en el puerto 55919, con una base de 100 usuarios y 100 000 tareas (50 000 completadas, datos sintéticos y determinísticos). Cinco binarios de la API (R0 a R5), publicados desde esas copias, cada uno en su puerto (55920 a 55924) contra la misma base. Readiness y consultas en cada etapa (sección 4). Al terminar: APIs detenidas, contenedor borrado.
  - **EX-SRC:** clon sparse de `dotnet/efcore` en `release/10.0` (commit `39e202b6b11e054eb77cb36e1a78a07aa51eded3`) en `...\scratchpad\src\efcore`.

## Glosario corto

- **Expand/contract** (en inglés, "parallel change"): cambiar un esquema en fases. Primero se agrega lo nuevo (expand), después se migran datos y lectores (migrate), y al final se quita lo viejo (contract). Fowler lo define como "three distinct phases: expand, migrate, and contract" ([Fowler, Parallel Change](https://martinfowler.com/bliki/ParallelChange.html)).
- **Rolling deploy** (despliegue por tandas): se reemplazan instancias de a poco. Durante un rato conviven la versión vieja y la nueva, contra la misma base. GitLab lo describe así: "different application instances might temporarily run different code versions" ([GitLab, Avoiding downtime](https://docs.gitlab.com/development/database/avoiding_downtime_in_migrations/)).
- **Backfill**: llenar una columna nueva con datos para las filas que ya existían.
- **Dual write** (escritura doble): el código nuevo escribe en la columna vieja y en la nueva a la vez.
- **Shadow property** (propiedad de sombra): una columna que EF mapea sin que exista la propiedad en la clase C#. Se declara con `builder.Property<bool>("Nombre")`.
- **ACCESS EXCLUSIVE**: el bloqueo más fuerte de una tabla en PostgreSQL. Frena lecturas y escrituras ([PostgreSQL 18, Explicit Locking](https://www.postgresql.org/docs/18/explicit-locking.html)).
- **SHARE UPDATE EXCLUSIVE**: bloqueo que deja leer y escribir, pero frena cambios de esquema, `VACUUM` y `ANALYZE` sobre la tabla ([PostgreSQL 18, Explicit Locking](https://www.postgresql.org/docs/18/explicit-locking.html)).
- **`CREATE INDEX CONCURRENTLY`**: crea un índice sin frenar escrituras. No puede correr dentro de una transacción ([PostgreSQL 18, CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html)).
- **Advisory lock** (candado de aplicación): candado que toma la aplicación en PostgreSQL con un número. Acá evita que dos instancias migren a la vez.
- **Analyzer Roslyn**: código que el compilador de C# ejecuta durante el build y que puede marcar error. Así funcionan las reglas DUNE0001 a DUNE0017.
- **Baseline** (línea base o excepciones): lista de cosas viejas que una regla nueva deja pasar sin tocarlas.

## Conclusión

1. **Sí, el orden es agregar, escribir las dos, copiar, cambiar la lectura, dejar de escribir y borrar. Cada etapa va en su release.** Un release nunca hace un cambio destructivo que el código vivo todavía usa. La razón es el rolling deploy: la versión vieja sigue corriendo contra la base nueva ([Fowler](https://martinfowler.com/bliki/ParallelChange.html); [GitLab](https://docs.gitlab.com/development/database/avoiding_downtime_in_migrations/)).
2. **Candidato recomendado: `TaskItem.IsCompleted` (bool) → `CompletedAt` (timestamp nullable).** Motivo real en el código: `UpdateTask` ya calcula el momento de completar (`UpdateTask.cs:47`) y solo lo manda a una métrica (`UpdateTask.cs:64`). El contrato HTTP no cambia, porque `isCompleted` sigue en el JSON. El candidato cubre las cuatro etapas del plan.
3. **Costo del candidato:** el backfill es determinista pero aproximado. Para una tarea completada se usaría `COALESCE(UpdatedAt, CreatedAt)`. Eso no siempre es la fecha real de completado: si la tarea se renombró después, `UpdatedAt` es posterior. Tobi tiene que aceptar esa aproximación (decisión 1).
4. **Secuencia medida:** R1 agrega la columna, R2 hace el backfill, R3 cambia la lectura, R4 deja de escribir la bool con un default y R5 la borra. Cada etapa que corrí tuvo readiness en verde (R3 no la corrí como binario). R5 mostró el problema central: la API vieja sigue "ready" aunque sus consultas fallen. Readiness no prueba que el código viejo funcione.
5. **Arranque con código viejo:** EF 10 ignora las migraciones aplicadas que el ensamblado no conoce. No tira error ni avisa, y readiness queda en verde. Lo medí: R0 arrancó contra una base con una migración que no conocía (sección 3).
6. **Verificación recomendada:** un test xUnit que avanza la base con `IMigrator`, inserta filas con el SQL de la versión vieja y comprueba datos y readiness en cada etapa. Más una corrida única con binarios reales, como la de este informe, guardada en `artifacts/`.
7. **Gate recomendado:** un analyzer Roslyn propio (`DUNE0018`, nombre a confirmar) con cuatro reglas: nada de `RenameColumn` ni `RenameTable` fuera de una migración de contrato; los `Drop*` solo en una migración `Contract` que no tenga nada más; un `CreateIndex` sobre tabla existente debe ser concurrente; un `AlterColumn` a NOT NULL sobre tabla existente es error. Alternativa de terceros: MigrationGuard (MIT, versión 0.x).
8. **Paso 18:** no editar `AddTaskListIndex`. EF identifica las migraciones aplicadas solo por id, así que editarla no cambia nada en las bases donde ya corrió. Va en una lista explícita de excepciones de la regla, con seis ids y su motivo.
9. **Decisiones para Tobi:** aceptar el backfill aproximado, decidir si el ciclo queda en `main` o solo en el ensayo, y elegir la herramienta del gate. Lista completa al final.

## Correcciones que trajo el ensayo (agregado al implementar)

El primer ensayo con binarios reales (`artifacts/verify-template/paso19-ensayo-rojo/`) siguió las secciones 2.5 y 4.2 de este informe y falló en dos etapas. La implementación se aparta de este informe en esos dos puntos (`docs/decisions.tsv`):

- **El backfill tiene que ir en las dos direcciones.** La sección 4.2 llena solo las completadas sin fecha. En la etapa 1, el release 1 completa una tarea (escribe las dos columnas) y el release 0, todavía vivo, la reabre tocando solo `IsCompleted`. La tarea queda con `IsCompleted = false` y `CompletedAt` con fecha, y desde la etapa 3 se lee completada. El backfill implementado pone `NULL` en ese caso.
- **La propiedad de sombra no deja de usar la columna.** La sección 2.5 deja `IsCompleted` como propiedad de sombra con default en la etapa 4. EF la sigue nombrando en cada `SELECT` y en el `RETURNING` del `INSERT`, así que el `DROP` de la etapa 5 le dio 500 al release 4 mientras convivían. La implementación pone el default en la migración Expand, saca la columna del modelo en la etapa 4 y mueve a la migración Contract el `DropColumn` que la CLI genera en la etapa 4.

## 1. Expand/contract y por qué "nunca destructivo en el mismo release"

**Respuesta:** un cambio de esquema que rompe código viejo se parte en releases. Cada release es compatible con el anterior. El borrado va al final, cuando ningún código vivo lo usa.

**Fuentes de la regla:**

- Fowler define la fase de contract como "remove the old version and change the interface so that it only supports the new version" ([Fowler](https://martinfowler.com/bliki/ParallelChange.html)).
- GitLab para columnas: "Ignoring and dropping columns should not occur simultaneously in the same release." Su secuencia son tres releases: ignorar la columna en M, borrarla en M+1 y quitar la regla de ignorar en M+2. La razón es que "dropping a column is a destructive operation that can't be rolled back easily" ([GitLab](https://docs.gitlab.com/development/database/avoiding_downtime_in_migrations/)).
- GitLab para renombrar: "Renaming columns the standard way requires downtime." El camino sin corte usa una migración normal y una post-deployment (migración que corre después del despliegue de la app). Sirve solo para tablas chicas; las grandes usan migraciones en segundo plano por lotes ([GitLab](https://docs.gitlab.com/development/database/avoiding_downtime_in_migrations/)).
- strong_migrations (Rails, licencia MIT) recomienda para un rename: agregar la columna nueva, escribir en las dos, hacer backfill, pasar las lecturas y borrar la vieja al final ([strong_migrations](https://github.com/ankane/strong_migrations)).

**Secuencia profesional para un cambio de columna (lo que dicen las fuentes, con el orden que medí en la sección 4):**

| Etapa | Qué pasa en la base | Por qué no puede ir antes |
|---|---|---|
| Expand | Se agrega la columna nueva, nullable y sin default volátil. | El código viejo no la mapea, así que no la nota. |
| Dual write | El código nuevo escribe en la vieja y en la nueva. | Sin esto, cada escritura nueva deja un hueco. |
| Backfill | Se llenan las filas viejas. | Tiene que correr cuando ninguna instancia vieja escribe sola la columna vieja. |
| Switch reads | El código lee la nueva. | Solo después del backfill. |
| Stop writing | El código deja de escribir la vieja. La base le pone default a la vieja. | El default evita que las escrituras nuevas fallen por NOT NULL. |
| Contract | Se borra la vieja, en un release aparte. | Ningún código vivo la mapea. |

**Por qué importa en PostgreSQL:**

- `ALTER TABLE` toma `ACCESS EXCLUSIVE` "unless explicitly noted" ([PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)). Ese bloqueo frena también las lecturas.
- `ADD COLUMN` con default no volátil no reescribe la tabla, porque "the default value is evaluated at the time of the statement and the result stored in the table's metadata" ([PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)). Un default volátil sí reescribe la tabla completa.
- `DROP COLUMN` no reescribe la tabla: "simply makes it invisible to SQL operations" ([PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)). Pero el borrado no se deshace.
- `SET NOT NULL` solo se puede aplicar si ninguna fila tiene NULL ([PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)).
- [EXPERIMENTO EX-DB] Con la base del ensayo, un INSERT que no nombra `IsCompleted` falla con `null value in column "IsCompleted" ... violates not-null constraint` (SQLSTATE 23502). Eso pasa antes de la etapa Stop writing, si la columna no tiene default.

## 2. EF Core 10: qué genera cada cambio

**Respuesta:** EF genera la operación de la diferencia entre el modelo y el snapshot. Cuatro casos cambian el significado de los datos sin avisar: el rename por estructura, el default que inventa valores en filas viejas, el `AlterColumn` a NOT NULL y el borrado con `Down` destructivo.

Cada caso se corrió en EX-EF con `dotnet ef migrations add` sobre una copia de `backend/`. Después se borró con `migrations remove`.

**2.1 Quitar una propiedad: `DropColumn` en la misma migración (E1) [EXPERIMENTO]**

- Quité `IsCompleted` del modelo y agregué `CompletedAt`. EF generó `DropColumn("IsCompleted")` y `AddColumn("CompletedAt", timestamptz, nullable: true)` en la misma migración.
- El `Down` vuelve a agregar `IsCompleted` como `boolean NOT NULL DEFAULT false`. Todas las tareas quedan no completadas: se pierde el dato.
- EF no hace diferencia entre "quitar la propiedad" y "ignorar el mapeo": en ambos casos el modelo no tiene la columna. [INFERENCIA, por el mecanismo del modelo]
- Qué hacer: no mezclar el borrado con el código que lo deja de usar (sección 1).

**2.2 Renombrar una propiedad o una columna (E2a, E2b) [EXPERIMENTO]**

- Con `HasColumnName("Name")` sobre la misma propiedad, EF genera `RenameColumn("Title" → "Name")` (E2a).
- Con un rename de la propiedad C# (`Title` → `Name`, sin `HasColumnName`), EF también genera `RenameColumn` (E2b). Yo esperaba `DropColumn` + `AddColumn`, que borraría datos.
- La razón está en el código. `MigrationsModelDiffer.DiffCollection` empareja columnas viejas y nuevas con una lista de predicados, en orden. El último predicado solo compara la estructura de la columna (tipo, nulabilidad, largo) ([EF Core, MigrationsModelDiffer.cs, release/10.0](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/MigrationsModelDiffer.cs)). Si una columna se borra y otra del mismo tipo se agrega en la misma migración, EF las trata como un rename.
- [INFERENCIA desde el código, no medido] Por eso un `bool NOT NULL` que se reemplaza por otro `bool NOT NULL` con otro significado se generaría como `RenameColumn`. El dato quedaría con el significado viejo.
- Qué hacer: el gate tiene que tratar `RenameColumn` como destructivo, aunque lo genere EF solo (sección 6).

**2.3 Cambiar nulabilidad (E2c) [EXPERIMENTO]**

- `UpdatedAt` pasa de nullable a requerido. EF genera `AlterColumn<DateTimeOffset>(nullable: false, defaultValue: new DateTimeOffset(0001-01-01))`.
- EF pone un valor inventado en las filas viejas que tengan NULL. Eso es datos inventados. Hay que hacer el backfill antes, a mano.

**2.4 Agregar una columna requerida a una tabla con filas (E2d) [EXPERIMENTO]**

- Una `bool` requerida nueva genera `AddColumn<bool>(nullable: false, defaultValue: false)`. Un `string` requerido genera `defaultValue: ""`.
- EF llena las filas viejas con ese default sin avisar. Es un dato inventado, igual que en 2.3.
- Qué hacer: agregar la columna nullable (expand), hacer el backfill real, y recién después poner NOT NULL.

**2.5 Default para escritores que no mapean la columna (E2e, EX-DB) [EXPERIMENTO]**

- `builder.Ignore(t => t.IsCompleted); builder.Property<bool>("IsCompleted").HasDefaultValue(false);` genera solo `AlterColumn<bool>(defaultValue: false)`. No toca los datos.
- Sin ese default, un INSERT del código nuevo (que no mapea la columna) falla con 23502 (sección 1). Con el default, el INSERT entra y la columna queda en `f`.
- Esto es lo que usa la etapa Stop writing de la secuencia (sección 4).
- El mecanismo lo medí. No leí una página de Microsoft que describa `HasDefaultValue` sobre una propiedad de sombra. [NO COMPROBADO como fuente]

**2.6 `migrationBuilder.Sql(..., suppressTransaction: true)` (E2f) [EXPERIMENTO]**

- Escribí a mano un `Sql("CREATE INDEX CONCURRENTLY ...", suppressTransaction: true)` dentro de una migración generada por la CLI. `dotnet ef migrations script` lo saca fuera del `START TRANSACTION`/`COMMIT`, y el historial se inserta después.
- EF documenta que las migraciones pueden llevar SQL escrito a mano ("Manual migration customization") ([EF Core, Data seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)). Eso choca con `docs/conventions.md:91` ("No se editan a mano"). Decisión 5.

**2.7 `IsCreatedConcurrently()` (Npgsql), medido en el paso 18**

- Npgsql documenta `IsCreatedConcurrently()` y advierte "Do not enable this feature before reading the PostgreSQL documentation" ([Npgsql, Indexes](https://www.npgsql.org/efcore/modeling/indexes.html)).
- En el paso 18 medí que la migración que genera con esa opción hace `DROP INDEX` y `COMMIT` antes de `CREATE INDEX CONCURRENTLY`. El índice viejo se borra antes de que el nuevo exista. Ver [paso 18, sección 1](paso-18-indices-medidos.md).

**2.8 Dónde va el backfill: SQL en la migración o job por lotes**

- [EXPERIMENTO EX-DB] R2 corrió `UPDATE "TaskItems" SET "CompletedAt" = COALESCE(...) WHERE "IsCompleted" AND "CompletedAt" IS NULL` como parte de la migración, dentro de la transacción de EF. En 100 000 filas actualizó 50 000 y no quedaron huecos.
- [INFERENCIA] A la escala de este repo (un template con datos chicos) alcanza una sola sentencia en la migración. Con millones de filas, GitLab recomienda migraciones en segundo plano por lotes ([GitLab](https://docs.gitlab.com/development/database/avoiding_downtime_in_migrations/)).
- No medí el tiempo del backfill por separado. Quedó sin comprobar.

**2.9 Timeouts al arrancar**

- El timeout por comando de Npgsql es 30 segundos por defecto ([Npgsql, Connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)). `DatabaseInitializer` usa la misma cadena de conexión.
- [INFERENCIA, no medido] Un backfill de más de 30 s haría fallar el arranque de la API. Por eso el paso 18 ya marcó el riesgo de `DROP INDEX`.

**2.10 Dos candados al migrar**

- EF 9 y posteriores toman un lock propio al migrar con `MigrateAsync` ([EF Core, Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)). La inicialización del repo ya toma su `pg_advisory_lock` (`DatabaseInitializer.cs:20`).
- [NO COMPROBADO] No revisé si hay algún problema al tomar los dos candados. Queda como pendiente (sección de pendientes).

## 3. Código viejo contra esquema nuevo (rollback y rolling deploy)

**Respuesta:** EF 10 no tira error, no avisa y no pone la readiness en rojo. Lo confirmé en el código y en la corrida.

**En el código (EF Core, `release/10.0`):**

- `Migrator.PopulateMigrations` separa las migraciones del ensamblado en dos listas: las aplicadas y las no aplicadas. Recorre las del ensamblado y pregunta si su id está en el historial ([EF Core, Migrator.cs](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/Migrator.cs)). Un id del historial que el ensamblado no tiene no entra en ninguna lista.
- `GetPendingMigrationsAsync` es "las del ensamblado menos las aplicadas" ([EF Core, RelationalDatabaseFacadeExtensions.cs](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Extensions/RelationalDatabaseFacadeExtensions.cs)). Por eso las migraciones desconocidas no cuentan como pendientes.
- Con eso, `PendingMigrationsHealthCheck` (`PendingMigrationsHealthCheck.cs:18`) da 0 y el check queda en `healthy`.

**En la corrida [EXPERIMENTO EX-DB]:**

- R0 (el código de main) arrancó contra la base con R1 aplicado (la migración `ExpandTaskCompletedAt` que R0 no conoce). Readiness: `{"status":"ready",...,"checks":{"database":"healthy","migrations":"healthy"}}`, HTTP 200.
- R0 se reinició desde cero contra la base con R1 y R2 aplicados. Mismo resultado. El log de arranque tiene seis líneas y ninguna advertencia. `appsettings.json` deja `Microsoft.EntityFrameworkCore` en `Warning`, así que una advertencia de EF se habría visto.
- Un detalle: R0 se arrancó primero contra la base vieja, y luego se reinició. Como EF no avisa, el aviso no aparece en ningún caso.

**Consecuencia:** readiness en verde no prueba que el código viejo funcione. Eso lo muestra el R5 (sección 4).

## 4. Qué cambiar para ensayar: la secuencia del candidato

### 4.1 Candidatos evaluados

| Candidato | Motivo real en el repo | Backfill | Cambia contrato HTTP | Cubre agregar, backfill, cambiar código, borrar | Veredicto |
|---|---|---|---|---|---|
| A. `IsCompleted` (bool) → `CompletedAt` (timestamp nullable) | Sí: `UpdateTask.cs:47` y `:64` calculan el momento de completar y solo lo mandan a una métrica. | Aproximado: `COALESCE(UpdatedAt, CreatedAt)`. | No: `isCompleted` se deriva de `CompletedAt`. | Sí, las cuatro. | **Recomendado**, con la aproximación aceptada por Tobi. |
| B. Índice `(OwnerId, CreatedAt, Id)` con `CONCURRENTLY` y borrado del viejo en otro release | Sí: el pendiente del paso 18 (`progreso.md`). | No aplica. | No. | No: no agrega ni borra columnas. Cubre solo "crear antes de borrar". | Buen complemento, no reemplaza el ensayo de columnas. |
| C. Renombrar `Title` → `Name` con `HasColumnName` | No hay motivo de dominio en `CONTEXT.md` ni en el código. | No aplica. | No. | No: sin backfill. | Rechazado: rename sin motivo. |
| D. `UpdatedAt` nullable → requerido, con backfill `CreatedAt` | No. | Inventa significado: "nunca editada" no es "creada". | No. | Sí, pero con datos inventados. | Rechazado (sección 2.3). |

[INFERENCIA] A es el único candidato con motivo real, backfill que no es nulo y sin cambio de contrato. Por eso lo recomiendo, con la aclaración del backfill.

### 4.2 Secuencia release por release

El contrato HTTP no cambia: `TaskResponse` sigue con `isCompleted` (`TaskResponses.cs:9`) y el frontend no cambia (`frontend/src/app/features/tasks/api/task-response.ts:4`). La regla 3 de `AGENTS.md` no se dispara.

| Release | Migración (nombre de ensayo) | Código | Código viejo contra este esquema | Verificado |
|---|---|---|---|---|
| R0 (main hoy) | Hasta `AddTaskListIndex`. | Escribe y lee `IsCompleted`. | Es el código viejo. | [EXPERIMENTO] Readiness 200. |
| R1 Expand | `ExpandTaskCompletedAt`: `AddColumn CompletedAt timestamptz NULL`. | Escribe las dos: `UpdateTask` pone `CompletedAt = now` al completar y `null` al desmarcar. Lee `IsCompleted`. | R0 sigue funcionando: su INSERT con sus columnas entra. Lo medí. | [EXPERIMENTO] Readiness 200 de R0 y de R1. |
| R2 Backfill | `BackfillTaskCompletedAt`: `UPDATE ... SET "CompletedAt" = COALESCE("UpdatedAt","CreatedAt") WHERE "IsCompleted" AND "CompletedAt" IS NULL`. | Igual que R1. | R0 todavía vive: si completa una tarea después del backfill, deja un hueco (`IsCompleted = true`, `CompletedAt = NULL`). | [EXPERIMENTO] 50 000 filas, 0 huecos al terminar. Con R0 vivo, un UPDATE de R0 deja 1 hueco. Readiness 200. |
| R3 Switch reads | Sin migración. | Lee `CompletedAt`: el booleano que se responde es `CompletedAt != null`. Sigue escribiendo las dos. | Un R2 que todavía lee `IsCompleted` queda bien mientras R3 escriba las dos. | [NO COMPROBADO] No corrí el binario de R3. Sí medí la expresión derivada en SQL: con el hueco, la diferencia entre el booleano y la derivada es 1. |
| R4 Stop writing | `StopWritingIsCompleted`: `AlterColumn<bool>(defaultValue: false)`. Sin tocar datos. | Deja de mapear `IsCompleted` (shadow property con default, sección 2.5). | Un R3 vivo todavía lee y escribe la columna, así que no se rompe. Un writer sin el mapeo sin default falla con 23502. | [EXPERIMENTO] Con el default, el INSERT sin la columna entra. Readiness 200 de R2 (vieja) y de R4. |
| R5 Contract | `DropIsCompleted`: `DropColumn("IsCompleted")`. El `Down` vuelve a agregar la columna con `false` y pierde el dato. | Sin `IsCompleted`. | R2, que todavía mapea la columna, falla sus consultas: `column "IsCompleted" does not exist` (42703). | [EXPERIMENTO] Readiness de R2 sigue en 200, con sus consultas rotas. |

Orden obligatorio: R2 solo cuando ninguna instancia de R0 viva; R3 completo antes de R4; R4 completo antes de R5.

Qué muestra la corrida de R5: readiness no detecta código viejo roto. Por eso la verificación del paso no puede ser solo readiness (sección 5).

### 4.3 El riesgo del R4 (lo que pasa si el orden no se respeta)

- Si R4 entra antes de que R3 esté completo, una lectura de `IsCompleted` ve `false` en una tarea que sí está completa (el R4 escribe solo `CompletedAt`). [INFERENCIA desde el default de la sección 2.5 y la lectura de R2]
- Por eso el orden R3 → R4 → R5 no es opcional.

## 5. Cómo verificar el ciclo (ciclo probado sobre una base con datos)

**Respuesta:** un test xUnit que avanza la base migración por migración con `IMigrator`, con datos reales en cada etapa, más una corrida única con binarios reales. Solo el test corre en CI.

**Lo que no pude confirmar:** cómo prueban los mantenedores de GitLab, Rails o EF este ciclo. Las fuentes que leí describen la regla y el orden, pero no su tooling de pruebas. [NO COMPROBADO]

**Opción A: test xUnit de integración (recomendada para CI)**

- Base propia con `TestDatabases.CreateEmptyAsync`, como en `ConcurrentStartupTests` (`backend/Tests/Startup/ConcurrentStartupTests.cs`).
- Migrar a una migración objetivo con `IMigrator`. EF documenta el acceso con `GetInfrastructure().GetService<IMigrator>()` para escenarios avanzados ([EF Core, Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)). El `Migrator` usa un id de migración objetivo dentro de `PopulateMigrations` (`Migrator.cs`). No revisé la firma pública de `IMigrator.Migrate` ni la probé. [NO COMPROBADO]
- Por etapa: insertar filas con el SQL de la versión vieja (el mismo que el interceptor captura en el paso 18), avanzar a la etapa siguiente y afirmar datos (backfill completo, sin huecos) y readiness con el `PendingMigrationsHealthCheck` real.
- Limitación: no corre el binario viejo. Usa el SQL de la versión vieja. [INFERENCIA]

**Opción B: corrida con binarios reales (la de este informe)**

- Publicar el código de cada release, arrancarlo contra la misma base con datos y pedir readiness. Correrla como script y guardar los logs en `artifacts/verify-template/<id>/`.
- Es la prueba más fiel: muestra lo que pasa con código viejo de verdad. Cuesta más (Docker, varios binarios y minutos) y no va en CI.

**Recomiendo A en CI y B como evidencia del paso una vez** (como en este informe). Así el gate de CI es determinista, y la prueba de código viejo queda documentada.

Para A hay que resolver un detalle: el test no puede tener cinco modelos de EF a la vez. Por eso usa SQL para simular al código viejo. Eso lo deja como [INFERENCIA]: no lo he escrito ni corrido.

## 6. Gate: hacer ejecutable la regla (AGENTS regla 4)

### 6.1 Cómo están escritas las reglas hoy

- Las reglas C# son analyzers Roslyn en `dune/Dune.Analyzers/`, con ids `DUNE0001` a `DUNE0017` (`dune/Dune.Analyzers/DuneDiagnostics.cs`). Cada una tiene su test en `dune/Dune.Analyzers.Tests/` con `AnalyzerHost`.
- Las migraciones ya tienen un camino aparte: `DunePaths.IsMigration` (`dune/Dune.Analyzers/DunePaths.cs:19`) y `OneTypePerFileAnalyzer` las exime (`OneTypePerFileAnalyzer.cs`). Hay un test de eximición (`OneTypePerFileAnalyzerTests.MigrationsAreExempt`).
- Las reglas de archivos son scripts Node en `dune/*.mjs` con `*.test.mjs` que arman repos temporales (`dune/check-structure.test.mjs`). `check-all.mjs` los corre (`.agents/skills/verify-template/scripts/check-all.mjs:72-83`).

### 6.2 Reglas propuestas (un analyzer, cuatro reglas)

Cada regla necesita una fixture válida y una violación deliberada (regla 4 de `AGENTS.md`). Las reglas aplican solo a `backend/Infrastructure/Persistence/Migrations/` y a `Features/*/Persistence/Migrations/`.

| Regla | Qué marca | Por qué (fuente) | Fixture válida | Violación deliberada |
|---|---|---|---|---|
| R-A: sin rename | `RenameColumn` o `RenameTable` en cualquier migración, salvo en una `Contract` que no tenga nada más. | Un rename rompe el código vivo (GitLab, strong_migrations). EF lo genera solo al emparejar columnas (sección 2.2). | `AddColumn` nullable. | `RenameColumn` en una migración normal. |
| R-B: `Drop*` solo en `Contract` | `DropColumn`, `DropTable`, `DropIndex` solo si el nombre de la clase contiene `Contract`, y la migración no tiene otra operación (ni `Sql`, ni `AddColumn`, ni `CreateIndex`). | "Ignoring and dropping columns should not occur simultaneously in the same release" (GitLab). | Migración `ContractDropX` con solo `DropColumn`. | `DropColumn` junto con `AddColumn` en una migración normal. |
| R-C: índice concurrente | `CreateIndex` sobre una tabla que no se crea en la misma migración debe llevar la marca de concurrencia de Npgsql. | Con índice normal, "table updates are blocked, but reads are still allowed" (squawk, regla `require-concurrent-index-creation`, [README de squawk](https://github.com/sbdchd/squawk)). | `CreateIndex` sobre una tabla nueva. | `CreateIndex` sobre `TaskItems` sin la marca. |
| R-D: NOT NULL sobre existente | `AlterColumn` con `nullable: false` sobre una columna que ya existía. | EF inventa un default (sección 2.3). PostgreSQL exige que no haya NULL (PostgreSQL, ALTER TABLE). | `AlterColumn` que sube un `nullable` a `true`. | `AlterColumn` a `nullable: false` en una migración sin backfill. |

[NO COMPROBADO] El nombre exacto de la anotación de concurrencia de Npgsql en el código generado. Hay que leerlo de una migración real antes de escribir la regla R-C.

Reglas que no propongo como error: `AddColumn` no nullable con `defaultValue` (sección 2.4) y `Sql` con `UPDATE` o `DELETE` mezclado con DDL. Las dos son advertencias.

### 6.3 Baseline: migraciones que hoy violarían la regla

- `AddTaskListIndex` (paso 18): `DropIndex` y `CreateIndex` sin concurrencia. Viola R-B y R-C.
- `ServerSideSessions`: borra `RefreshTokens` sin backfill (`docs/decisions.tsv:94`). Viola R-B.
- Las otras cuatro (`InitialCreate`, `AddTaskVersion`, `AddIdempotencyKeys`, `AddAuditEntries`) pasan. [INFERENCIA desde la lista de operaciones de cada una; revisé solo `AddTaskVersion` y `AddTaskListIndex` en el código.]

Propuesta: una lista explícita de ids en `dune/` con el motivo de cada uno. Así la regla queda sin excepciones ocultas, y cada excepción tiene motivo. Un id nuevo en esa lista necesita una fila en `docs/decisions.tsv` (regla 9 de `AGENTS.md`).

### 6.4 Herramientas de terceros: licencias y qué necesitan

| Opción | Licencia (verificada) | Qué hace | Ganancia | Pérdida | Qué necesita aquí |
|---|---|---|---|---|---|
| Analyzer propio (DUNE) | Licencia del repo. | Cuatro reglas en Roslyn, como el resto de Dune. | Sin dependencias nuevas. Mismo estilo que DUNE0001 a DUNE0017. | Hay que escribir y mantener cada regla con su fixture. | Clase en `dune/Dune.Analyzers/`, test en `dune/Dune.Analyzers.Tests/`. |
| MigrationGuard ([GitHub, Mefju08/Migration-Analyzer](https://github.com/Mefju08/Migration-Analyzer)) | MIT. | Analiza migraciones de EF Core y falla el build. Tiene 18 reglas: MC0001 (drop columna), MC0002 (rename), MC0003 (índice sin concurrencia en PostgreSQL), MC0018 (drop de índice, incluido el implícito que EF hace al reemplazar uno). | Reglas ya escritas. Según un resumen de búsqueda (no leí la página del paquete), el paquete `Testing` corre sin base. [NO VERIFICADO] | Versión 0.x. La página muestra ejemplos con `net8.0` y no dice qué versión de EF Core soporta. Otra dependencia. | Paquete en `Tests`, reglas configuradas y fixtures de todas formas. |
| squawk ([GitHub, sbdchd/squawk](https://github.com/sbdchd/squawk)) | Apache-2.0 o MIT (a elección). | Lint de SQL. Marca `ban-drop-column` y `require-concurrent-index-creation`, entre otras. | Reglas de bloqueo de PostgreSQL bien probadas. | Lee SQL, no C#. Hay que generar el SQL con `dotnet ef migrations script` por rango y correrlo como binario o imagen Docker. Otro paso de CI. | Script que genere SQL por migración nueva y lo pase a squawk. |
| pgroll ([GitHub, xataio/pgroll](https://github.com/xataio/pgroll)) | Apache-2.0. | Ejecuta expand/contract solo, con sus propias migraciones JSON y vistas de compatibilidad. | Maneja el protocolo completo. | Reemplaza las migraciones de EF, que la convención usa como fuente (`conventions.md:91`). | No lo recomiendo aquí. [INFERENCIA] |
| StewardEF ([NuGet](https://www.nuget.org/packages/stewardef)) | No aparece en la página. | Es una herramienta para aplastar (squash) y convertir migraciones, no un linter. | No aplica. | No aplica. | No lo recomiendo para este gate. |

**Recomendación:** el analyzer propio (cuatro reglas). Es lo que el repo ya usa, no suma dependencias y las reglas salen de las convenciones del repo. MigrationGuard es la alternativa si Tobi prefiere no escribir reglas, pero es de la versión 0.x. squawk aporta reglas de bloqueo más finas, pero lee SQL y agrega un paso de CI; puede sumarse después como segunda capa. La elección es de Tobi (regla 5).

## 7. Paso 18: qué hacer con `AddTaskListIndex`

**¿Se puede editar?** No. Tres razones:

- La regla del repo: "Las migraciones se generan por CLI y son forward, con backfill. No se editan a mano." (`docs/conventions.md:91`). El paso 18 lo dijo igual: "no editarla" (`paso-18-indices-medidos.md`, sección 6).
- EF identifica las migraciones aplicadas solo por su id (`Migrator.PopulateMigrations`). Si se edita el código de una migración que ya corrió, las bases que la aplicaron no cambian. Las bases nuevas sí reciben la versión editada. El código y la base quedan con dos historias distintas. [INFERENCIA desde el código de EF; no medí el caso.]
- [NO COMPROBADO] No encontré una fuente de mantenedores que diga "no edites migraciones aplicadas" como principio. Lo que sí encontré es el comportamiento de EF y las recomendaciones de fix-forward de GitLab (migraciones nuevas, no cambiar las viejas).

**¿Cómo se trata el riesgo de bloqueo?**

- PostgreSQL: un `CREATE INDEX` normal "locks out writes (but not reads)" ([PostgreSQL 18, CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html)).
- Un `DROP INDEX` normal "acquires an ACCESS EXCLUSIVE lock on the table, blocking other accesses" ([PostgreSQL 18, DROP INDEX](https://www.postgresql.org/docs/18/sql-dropindex.html)). Eso confirma el pendiente del paso 18: también frena lecturas. La doc no nombra el lock de la forma concurrente.
- En el caso de `AddTaskListIndex`, el `DROP INDEX` y el `CREATE INDEX` van en la misma transacción. El bloqueo dura hasta el `COMMIT` ([EF Core, migraciones](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)). [INFERENCIA]

**Opción para el futuro (no para este paso):** en un release de expand, crear el índice nuevo con `CONCURRENTLY` (con `IsCreatedConcurrently()` o `Sql(..., suppressTransaction: true)`, sección 2.6–2.7). En un release de contract, borrar el viejo con `DROP INDEX CONCURRENTLY`. [INFERENCIA] `DROP INDEX CONCURRENTLY` no puede estar en una transacción ([PostgreSQL 18, DROP INDEX](https://www.postgresql.org/docs/18/sql-dropindex.html)). Cómo lo genera EF no lo probé.

**Qué hacer con la lista de excepciones (sección 6.3):** se dejan `AddTaskListIndex` y `ServerSideSessions` como excepciones explícitas, con su motivo. El riesgo de bloqueo se acepta hoy por la escala de la tabla (un template). Si Tobi quiere cerrarlo, es un paso aparte.

## Recomendación para este repo

1. **Ensayar el candidato A (`IsCompleted` → `CompletedAt`) en el ciclo de cinco releases de la sección 4.2.** Motivo: es el único con motivo real en el código, backfill no nulo y sin cambio de contrato HTTP.
2. **Verificación:** test xUnit de integración (opción A, sección 5) en CI, y una corrida con binarios reales (opción B) como evidencia de este paso, guardada en `artifacts/verify-template/<id>/`.
3. **Gate:** analyzer Roslyn propio con las cuatro reglas de la sección 6.2, cada una con fixture y violación, y la lista de excepciones de la sección 6.3.
4. **Convención:** agregar a `docs/conventions.md` la tabla de etapas de la sección 1, la regla de que un `Drop*` vive solo en una migración `Contract`, y la aclaración sobre SQL escrito a mano para el backfill (decisión 5). Cambia la línea 91.
5. **Paso 18:** no editar `AddTaskListIndex`. Dejarla como excepción explícita con motivo.
6. **No cambiar el dominio en `main` dentro de este paso** (recomendación fuerte, decisión 2). El ensayo prueba la secuencia; el cambio real de `IsCompleted` queda para un paso propio si Tobi lo quiere. Así el paso no amplía su alcance (regla 7).

## Decisiones abiertas

1. **Backfill aproximado para el candidato A.**
   - Aceptarlo: el backfill usa `COALESCE(UpdatedAt, CreatedAt)`. Gana un ciclo completo con datos. Pierde exactitud para tareas renombradas después de completarse, y lo documento en la migración.
   - No aceptarlo: no hay otra opción limpia. Dejar `CompletedAt` nulo para las completadas viejas obliga a cambiar cómo se deriva `isCompleted`, y eso ya no es el mismo ciclo.
   - Recomiendo **aceptarlo**, con la aproximación escrita en la migración y en `decisions.tsv`.
2. **Dónde queda el ciclo: en `main` o solo en el ensayo.**
   - En `main`: el repo tiene el cambio real de dominio. Gana un ejemplo completo. Pierde alcance: el paso se vuelve un cambio de modelo, y el plan dice "un paso = un cambio observable" (regla 1).
   - Solo en el ensayo: el repo cambia solo el gate, la convención y el test. Gana alcance acotado. Pierde que el dominio no cambia.
   - Recomiendo **solo en el ensayo** (punto 6 de la recomendación).
3. **Dónde vive la prueba.**
   - Test xUnit en CI más corrida única con binarios (recomendado): determinista en CI y evidencia real.
   - Solo script con binarios: más fiel, pero no va en CI y es lento.
4. **Herramienta del gate.**
   - Analyzer propio (recomendado): sin dependencias, mismo estilo que Dune, cuatro reglas.
   - MigrationGuard (MIT, 0.x): reglas hechas, dependencia nueva, versión inestable.
   - squawk (Apache-2.0 o MIT): reglas de bloqueo finas, lee SQL, agrega un paso de CI.
   - Decide Tobi (regla 5 de `AGENTS.md`).
5. **Convención 91: "No se editan a mano" frente al backfill.**
   - Mantener la regla tal cual: el backfill va en una migración aparte escrita a mano, fuera de la generada. Más trabajo, pero la regla queda limpia.
   - Aclarar la regla: el esquema lo genera la CLI, y el SQL de backfill se escribe a mano dentro de una migración generada y se marca. EF lo documenta así ("Manual migration customization"). Recomiendo **aclarar**.
6. **Paso 18 y la lista de excepciones.**
   - Dejar `AddTaskListIndex` como excepción y aceptar el riesgo de bloqueo por la escala del template. Recomiendo **esto**.
   - Hacer un paso aparte que cree el índice con `CONCURRENTLY` en un release y borre el viejo en otro.

## Pendientes fuera del alcance (regla 7)

- Readiness no detecta código viejo roto (R5). Eso toca el paso 16 (health checks). Sin paso asignado.
- EF Core: la herramienta es 10.0.2 y el runtime 10.0.12. El CLI avisa en cada comando. El paso 18 ya lo anotó y sigue abierto. Sin paso asignado.
- Timeout de 30 s por comando al arrancar con migraciones largas. Sin paso asignado.
- Dos candados al migrar (el `pg_advisory_lock` del repo y el lock de EF 9+). No revisé si se pisan. Sin paso asignado.

## Qué quedó sin comprobar

- No corrí el test xUnit propuesto (sección 5) ni el analyzer (sección 6). Son propuestas.
- No corrí el binario de R3 (switch reads). Solo medí en SQL la expresión que lo reemplaza.
- No probé requests HTTP con sesión (login y CSRF). El código viejo se simuló con SQL que copia el de EF, y con el arranque real de R0 y los readiness.
- El backfill se midió con 50 000 filas, no con millones. No medí su tiempo por separado.
- No medí bloqueos con carga concurrente. Los niveles de bloqueo vienen de la documentación de PostgreSQL, no de una medición.
- No verifiqué el nombre de la anotación de concurrencia de Npgsql en el código generado.
- No verifiqué la página de Prisma (la URL de la guía expand/contract dio 404). No la cito.
- No verifiqué que MigrationGuard o squawk corran sobre las migraciones de este repo.
- No verifiqué si MigrationGuard soporta EF Core 10.
- No verifiqué cómo EF empareja columnas con tipos que no sean `string` (mostré `string` y el caso de `bool` y `timestamp` quedó sin probar como rename).
- No verifiqué la fuente de "no editar migraciones aplicadas" como principio de los mantenedores (sección 7).
- La corrida usó EF 10.0.2 de herramienta y 10.0.12 de runtime. Es la misma situación que el paso 18.
- No revisé el CI de GitHub.
- Los datos del ensayo son sintéticos (100 000 tareas con fechas fijas). La precisión del backfill sobre datos reales es desconocida.

## Fuentes

- [Martin Fowler, Parallel Change (expand, migrate, contract)](https://martinfowler.com/bliki/ParallelChange.html)
- [GitLab, Avoiding downtime in migrations](https://docs.gitlab.com/development/database/avoiding_downtime_in_migrations/)
- [strong_migrations (Rails, MIT)](https://github.com/ankane/strong_migrations)
- [PostgreSQL 18, ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html)
- [PostgreSQL 18, Explicit Locking](https://www.postgresql.org/docs/18/explicit-locking.html)
- [PostgreSQL 18, CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html)
- [PostgreSQL 18, DROP INDEX](https://www.postgresql.org/docs/18/sql-dropindex.html)
- [Microsoft Learn, EF Core: Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- [Microsoft Learn, EF Core: Data seeding y migration customization](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [Npgsql, Indexes (IsCreatedConcurrently)](https://www.npgsql.org/efcore/modeling/indexes.html)
- [Npgsql, Connection string parameters (Command Timeout 30 por defecto)](https://www.npgsql.org/doc/connection-string-parameters.html)
- [dotnet/efcore, Migrator.cs (release/10.0)](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/Migrator.cs)
- [dotnet/efcore, MigrationsModelDiffer.cs (release/10.0)](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/MigrationsModelDiffer.cs)
- [dotnet/efcore, RelationalDatabaseFacadeExtensions.cs (release/10.0)](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Extensions/RelationalDatabaseFacadeExtensions.cs)
- [MigrationGuard, Mefju08/Migration-Analyzer (MIT)](https://github.com/Mefju08/Migration-Analyzer)
- [squawk, sbdchd/squawk (Apache-2.0 o MIT)](https://github.com/sbdchd/squawk)
- [pgroll, xataio/pgroll (Apache-2.0)](https://github.com/xataio/pgroll)
- [StewardEF (NuGet)](https://www.nuget.org/packages/stewardef)
