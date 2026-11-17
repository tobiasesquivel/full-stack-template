# Paso 18: índices medidos, no supuestos (investigación)

- Fecha: 2026-11-17.
- Alcance: el paso 18 del [plan](../plan.md) (`plan.md:355-358`). Concepto: plan de consulta verificado. Cambia: `backend/Features/Tasks/Persistence/TaskItemConfiguration.cs`, una migración de EF Core y un test de integración con `EXPLAIN`. Verifica: un test que afirma que la consulta de listado usa el índice esperado.
- Cómo está hoy:
  - La consulta de listado vive en `backend/Features/Tasks/ListTasks/ListTasks.cs:21-38`. Filtra `OwnerId == callerId`, agrega la comparación de filas `(CreatedAt, Id) < (cursor)` si hay cursor, ordena `CreatedAt DESC, Id DESC` y pide `pageSize + 1` filas.
  - El único índice de la tabla, además de la clave primaria, es `IX_TaskItems_OwnerId`: `builder.HasIndex(t => t.OwnerId)` en `TaskItemConfiguration.cs:29`, creado en `InitialCreate.cs:278-281`.
  - `OwnerId` es FK (clave foránea: la columna que apunta a otra tabla) a `UserProfiles.AuthId`, con borrado en cascada (`backend/Infrastructure/Persistence/AppDbContext.cs:39-43`).
  - La investigación del paso 7 ya midió que el índice `(OwnerId, CreatedAt, Id)` baja la página a 11 filas leídas (`paso-07-paginacion-keyset.md`, sección 4). Tobi decidió el 2026-11-03 dejar el índice para este paso (`docs/decisions.tsv:141`; `progreso.md:124`).
- Decisiones que ya están tomadas y que este informe no reabre: EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:36`); PostgreSQL 18 en tests (`backend/Tests/Support/PostgresFixture.cs:24`) y en Compose (`compose.yaml:3`); xUnit v3 con Testcontainers; la forma de la consulta keyset del paso 7; la forma de ADR-0004.
- Método: solo fuentes primarias. Son la documentación de PostgreSQL 18, Microsoft Learn (EF Core y su API), el código de `dotnet/efcore` en la rama `release/10.0`, la documentación de Npgsql (sitio y repo `npgsql/doc`) y use-the-index-luke de Markus Winand. Cada afirmación lleva su link. Lo que es deducción mía va marcado [INFERENCIA]. Lo que salió de un experimento va marcado [EXPERIMENTO].
- Evidencia propia (todo fuera del repo, en la carpeta temporal de la sesión):
  - **Experimento EF:** una copia de `backend/` sin `Tests/`. Le cambié `TaskItemConfiguration.cs` de cinco formas y corrí `dotnet ef migrations add` y `dotnet ef migrations script` en cada una. No necesita base.
  - **Experimento PG:** un contenedor descartable `postgres:18` (18.6) en el puerto 55918. Le apliqué el esquema real (`dotnet ef migrations script` desde cero). Un programa de consola llama a la clase real `ListTasks` con un `DbCommandInterceptor` (un interceptor de EF Core: código que EF llama antes de mandar cada comando a la base). El programa captura el SQL y los parámetros, y después corre `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` con esos mismos parámetros. Probé varios volúmenes y tres variantes de índices.
  - **Experimento test:** copié a la carpeta temporal el borrador de test que encontré en el worktree (sección 7) y lo corrí contra el contenedor, con el índice y sin él.
  - Al terminar bajé el contenedor. No toqué código del repo.

## Glosario corto

- **Plan de consulta:** los pasos que elige PostgreSQL para responder una consulta (qué índice usa, si ordena, cuántas filas lee). `EXPLAIN` lo muestra; `EXPLAIN ANALYZE` además ejecuta la consulta y muestra lo que pasó de verdad.
- **Planner** (planificador): la parte de PostgreSQL que elige el plan. Compara costos estimados a partir de estadísticas de la tabla.
- **Estadísticas:** lo que el planner sabe de los datos (cuántas filas, cuántos valores distintos por columna). Las junta `ANALYZE`.
- **Index Scan Backward:** recorrer un índice de atrás para adelante. Sirve un `ORDER BY ... DESC` sin ordenar.
- **Sort / top-N heapsort:** un paso de ordenamiento explícito. Para ordenar, primero tiene que leer todas las filas candidatas.
- **Bitmap Heap Scan:** primero junta en un mapa todas las filas que cumplen el filtro y después las lee de la tabla. No entrega las filas en orden.
- **Buffers:** bloques de 8 kB que la consulta tocó (`hit` = estaban en memoria, `read` = se leyeron de disco).
- **Heap:** la tabla en sí, donde están las filas completas. Un índice apunta al heap.
- **Covering index / `INCLUDE`:** un índice que guarda columnas extra para responder sin ir al heap. Eso se llama **index-only scan**.
- **Plan custom y plan genérico:** el custom se arma con los valores reales de los parámetros; el genérico sirve para cualquier valor y se reutiliza.

## Conclusión

- **Forma del índice:** `(OwnerId, CreatedAt, Id)`, todo ascendente, sin `INCLUDE`. En EF Core: `builder.HasIndex(t => new { t.OwnerId, t.CreatedAt, t.Id })`. EF lo llama `IX_TaskItems_OwnerId_CreatedAt_Id`. PostgreSQL lo recorre hacia atrás y entrega `CreatedAt DESC, Id DESC` sin ordenar. `DESC` en el índice no hace falta. `INCLUDE` no sirve, porque la consulta lee `xmin` (la versión de la fila) y una columna de sistema no puede entrar a un índice.
- **`IX_TaskItems_OwnerId` sobra.** El compuesto empieza por `OwnerId`, así que sirve también para el filtro por dueño y para el borrado en cascada (lo medí con `auto_explain`). Mantener los dos cuesta escrituras y espacio sin ganar nada.
- **EF Core lo saca solo.** Su convención de FK crea un índice para cada FK **salvo** que otro índice ya empiece por esas columnas. Lo confirmé en el código de EF Core 10 y con el experimento: reemplazar la línea genera `DropIndex("IX_TaskItems_OwnerId")` + `CreateIndex("IX_TaskItems_OwnerId_CreatedAt_Id")`. Dato de paso: hoy la línea `HasIndex(t => t.OwnerId)` es redundante con la convención; borrarla sola genera una migración vacía.
- **Medición (experimento PG, 100 dueños × 2 000 tareas, filas mezcladas en la tabla):** sin el índice, la página lee 2 000 filas, toca 1 189 buffers, ordena y tarda ~1 ms. Con el índice, lee 11 filas, toca 10-11 buffers, no ordena y tarda ~0,02 ms. Igual en la primera página y después de un cursor.
- **Test:** capturar el SQL real que manda EF con un `DbCommandInterceptor`, repetirlo con `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` y los mismos parámetros, y afirmar tres cosas: un nodo `Index Scan` sobre `IX_TaskItems_OwnerId_CreatedAt_Id` con dirección `Backward`, ningún nodo `Sort` y como mucho `pageSize + 1` filas en cada nodo. `EXPLAIN` funciona con los parámetros de Npgsql (experimento PG).
- **Datos del test:** base propia (`TestDatabases.CreateEmptyAsync`), unos miles de filas repartidas en varios dueños y `ANALYZE` explícito. Con tablas muy chicas el planner elige `Seq Scan` + `Sort` aunque el índice exista, y eso es correcto de su parte. Sin `enable_seqscan = off`: forzar el plan prueba que el índice **puede** usarse, no que el planner lo **elige**.
- **Sin herramientas nuevas.** Alcanza con `EXPLAIN (ANALYZE, BUFFERS)`, que viene con PostgreSQL. `pg_stat_statements`, `auto_explain` e HypoPG existen, pero este paso no las necesita.
- **Sin regla nueva de Dune.** El test es la regla ejecutable. Una regla general ("toda lista tiene un test de plan") no tiene hoy un segundo caso y costaría un gate nuevo.

## 1. Forma del índice para esta consulta

**Respuesta:** `(OwnerId, CreatedAt, Id)`, ascendente, sin `INCLUDE`.

**Orden de columnas**

- PostgreSQL usa un índice de varias columnas mejor cuando hay condiciones sobre las primeras: "equality constraints on leading columns, plus any inequality constraints on the first column that does not have an equality constraint, will always be used to limit the portion of the index that is scanned" ([PostgreSQL 18, Multicolumn Indexes](https://www.postgresql.org/docs/18/indexes-multicolumn.html)).
- En esta consulta, `OwnerId` es la igualdad y va primero. `(CreatedAt, Id)` es la clave de orden y la comparación del cursor, y va después. [INFERENCIA, a partir de la regla citada y de `ListTasks.cs:21-35`]
- Winand: "the column order of a concatenated index has great impact on its usability so it must be chosen carefully" y "a database can use a concatenated index when searching with the leading (leftmost) columns" ([use-the-index-luke, Concatenated Keys](https://use-the-index-luke.com/sql/where-clause/the-equals-operator/concatenated-keys)).
- El índice evita el ordenamiento si su orden coincide con el `ORDER BY`: "If the index order corresponds to the order by clause, the database can omit the explicit sort operation" ([use-the-index-luke, Indexed ORDER BY](https://use-the-index-luke.com/sql/sorting-grouping/indexed-order-by)). PostgreSQL lo llama "an important special case": `ORDER BY` con `LIMIT n`, donde "the first n rows can be retrieved directly, without scanning the remainder at all" ([PostgreSQL 18, Indexes and ORDER BY](https://www.postgresql.org/docs/18/indexes-ordering.html)).
- La comparación de filas `(CreatedAt, Id) < (@c, @i)` entra en la condición del índice. El experimento PG lo muestra: `Index Cond: (("OwnerId")::text = 'u1'::text) AND (ROW("CreatedAt", "Id") < ROW(...))`. [EXPERIMENTO]

**¿Hace falta `DESC`?** No.

- "a two-column index on (x, y): this can satisfy ORDER BY x, y if we scan forward, or ORDER BY x DESC, y DESC if we scan backward" ([PostgreSQL 18, Indexes and ORDER BY](https://www.postgresql.org/docs/18/indexes-ordering.html)). Winand: "Databases can read indexes in both directions" ([use-the-index-luke, ASC/DESC](https://use-the-index-luke.com/sql/sorting-grouping/order-by-asc-desc-nulls-last)).
- `DESC` hace falta solo con órdenes mezclados (`x ASC, y DESC`). Aquí `CreatedAt` e `Id` van los dos en `DESC`. La igualdad sobre `OwnerId` deja fijo el primer nivel, así que su dirección no importa. [INFERENCIA]
- El experimento PG con el índice ascendente dio `Index Scan Backward` sin nodo `Sort`. [EXPERIMENTO]
- EF Core 10 permite `IsDescending(false, true, true)` ([EF Core, Index sort order](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-sort-order)). El experimento EF generó `("OwnerId", "CreatedAt" DESC, "Id" DESC)`. Funcionaría igual, pero agrega una decisión que la consulta no necesita. [INFERENCIA]

**¿`INCLUDE` para un index-only scan?** No.

- Un index-only scan exige que "The query must reference only columns stored in the index" ([PostgreSQL 18, Index-Only Scans](https://www.postgresql.org/docs/18/indexes-index-only-scans.html)).
- La proyección lee `Id, Title, IsCompleted, CreatedAt, UpdatedAt` y `xmin` (`TaskResponses.cs:8-9`; `Version` se mapea a `xmin` desde el paso 8). PostgreSQL rechaza indexar `xmin`: `CREATE INDEX ... INCLUDE (xmin)` da `ERROR: index creation on system columns is not supported`. [EXPERIMENTO]
- Con `INCLUDE ("Title", "IsCompleted", "UpdatedAt")`, la consulta real sigue siendo `Index Scan Backward` (va al heap por `xmin`). Sin `xmin` en el `SELECT` pasaría a `Index Only Scan` con 4 buffers. El índice crece de 9,7 MB a 24 MB con 200 000 filas. [EXPERIMENTO]
- PostgreSQL avisa del costo: las columnas extra "duplicate data from the index's table and bloat the size of the index" y "B-tree deduplication is never used with indexes that have a non-key column" ([PostgreSQL 18, CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html)). También: "there is little point in including payload columns in an index unless the table changes slowly enough that an index-only scan is likely to not need to access the heap" ([PostgreSQL 18, Index-Only Scans](https://www.postgresql.org/docs/18/indexes-index-only-scans.html)). Las tareas cambian seguido.
- La página trae 11 filas: ir al heap por 11 filas cuesta unos 11 buffers en el peor caso. `INCLUDE` no compensa. [INFERENCIA, a partir del experimento]

**Cómo se declara en EF Core 10**

- `builder.HasIndex(t => new { t.OwnerId, t.CreatedAt, t.Id })` ([EF Core, Composite index](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#composite-index)).
- Nombre: "indexes created in a relational database are named `IX_<type name>_<property name>`. For composite indexes, `<property name>` becomes an underscore separated list of property names" ([EF Core, Index naming](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-naming-and-multiple-indexes)). En este repo el `<type name>` sale del nombre de la tabla: el experimento EF generó `IX_TaskItems_OwnerId_CreatedAt_Id`. [EXPERIMENTO]
- Migración que generó `dotnet ef migrations add` al reemplazar la línea [EXPERIMENTO]:

  ```csharp
  migrationBuilder.DropIndex(name: "IX_TaskItems_OwnerId", table: "TaskItems");
  migrationBuilder.CreateIndex(
      name: "IX_TaskItems_OwnerId_CreatedAt_Id",
      table: "TaskItems",
      columns: new[] { "OwnerId", "CreatedAt", "Id" });
  ```

  El `Down` hace lo inverso. El script SQL es `DROP INDEX "IX_TaskItems_OwnerId"; CREATE INDEX "IX_TaskItems_OwnerId_CreatedAt_Id" ON "TaskItems" ("OwnerId", "CreatedAt", "Id");`, dentro de una transacción.

**`CREATE INDEX CONCURRENTLY`: no en este paso**

- Un `CREATE INDEX` normal bloquea las escrituras de la tabla mientras se construye. Npgsql lo explica y ofrece `IsCreatedConcurrently()`, con la advertencia "Do not enable this feature before reading the PostgreSQL documentation" ([Npgsql, Creating indexes concurrently](https://www.npgsql.org/efcore/modeling/indexes.html#creating-indexes-concurrently)).
- `CREATE INDEX CONCURRENTLY` no corre dentro de una transacción: "a regular CREATE INDEX command can be performed within a transaction block, but CREATE INDEX CONCURRENTLY cannot" ([PostgreSQL 18, CREATE INDEX](https://www.postgresql.org/docs/18/sql-createindex.html#SQL-CREATEINDEX-CONCURRENTLY)).
- El experimento EF con `IsCreatedConcurrently()` generó: `DROP INDEX "IX_TaskItems_OwnerId"; COMMIT;` y **después** `CREATE INDEX CONCURRENTLY ...` fuera de la transacción. Entre las dos sentencias la tabla queda sin índice por dueño. [EXPERIMENTO]
- Para este repo: la API migra al arrancar, y la tabla es chica. Un `CREATE INDEX` normal alcanza. El orden seguro en producción (primero crear el nuevo, después borrar el viejo, en dos releases) es justo el concepto del paso 19 (expand/contract). Lo anoto como pendiente para ese paso, no lo hago acá (regla 7). [INFERENCIA]

## 2. ¿`IX_TaskItems_OwnerId` sobra?

**Respuesta:** sí. Conviene borrarlo en la misma migración, y EF Core lo hace solo al reemplazar la línea.

**PostgreSQL no indexa la FK solo, pero el compuesto la cubre**

- "the declaration of a foreign key constraint does not automatically create an index on the referencing columns". Igual recomienda indexarlas, porque "a DELETE of a row from the referenced table ... will require a scan of the referencing table for rows matching the old value" ([PostgreSQL 18, Foreign Keys](https://www.postgresql.org/docs/18/ddl-constraints.html#DDL-CONSTRAINTS-FK)).
- Un índice que empieza por `OwnerId` sirve para buscar por `OwnerId` solo (regla de columnas iniciales, sección 1).
- Borrado en cascada medido con `auto_explain` (100 dueños × 2 000 tareas, solo el índice compuesto): PostgreSQL corre `DELETE FROM ONLY "public"."TaskItems" WHERE $1 = "OwnerId"` y usa `Bitmap Index Scan on "IX_TaskItems_OwnerId_CreatedAt_Id"`, 2 000 filas, 3,4 ms. [EXPERIMENTO]
- Las demás lecturas de `TaskItems` buscan por `Id` (`GetTask`, `UpdateTask`, `DeleteTask`) y usan la clave primaria. Ninguna otra consulta filtra solo por `OwnerId` además del listado y la cascada. [INFERENCIA, por grep de `TaskItems` en `backend/Features`]

**Costo de mantener un índice redundante**

- Winand: "The number of indexes on a table is the most dominant factor for insert performance" y "The more indexes a table has, the slower the execution becomes" ([use-the-index-luke, Insert](https://use-the-index-luke.com/sql/dml/insert)). Sobre usar un índice de varias columnas en lugar de dos: "It not only saves storage space, but also the maintenance overhead for the second index" ([use-the-index-luke, Concatenated Keys](https://use-the-index-luke.com/sql/where-clause/the-equals-operator/concatenated-keys)).
- Experimento PG, insertar 50 000 tareas [EXPERIMENTO, medición gruesa: la tabla crecía 50 000 filas entre una corrida y la siguiente]:

  | Índices además de la PK | Buffers tocados por el `INSERT` |
  |---|---|
  | solo `OwnerId` (hoy) | 301 196 |
  | solo compuesto | 352 211 |
  | los dos | 503 064 |

- Tamaño con 200 000 filas: `IX_TaskItems_OwnerId` 1,4 MB, compuesto 9,7 MB, PK 8,4 MB, tabla 13 MB. El índice simple es chico porque PostgreSQL agrupa los valores repetidos ("deduplication", [PostgreSQL 18, B-Tree Deduplication](https://www.postgresql.org/docs/18/btree.html#BTREE-DEDUPLICATION)). [EXPERIMENTO]
- Con los dos índices, el planner elige el compuesto para el listado (mismo plan y mismos buffers que con el compuesto solo). El simple no aporta lecturas y suma escrituras. [EXPERIMENTO]

**Qué hace EF Core con la FK si se borra el `HasIndex` simple**

- La documentación: "By convention, an index is created in each property (or set of properties) that are used as a foreign key" ([EF Core, Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)).
- El código de EF Core 10 agrega la excepción. `ForeignKeyIndexConvention` es "A convention that creates indexes on foreign key properties unless they are already covered by existing indexes or keys" ([`ForeignKeyIndexConvention.cs:8-10`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/Metadata/Conventions/ForeignKeyIndexConvention.cs#L8-L10)).
  - `CreateIndex` no crea nada si una clave o un índice ya cubre la FK ([`:331-352`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/Metadata/Conventions/ForeignKeyIndexConvention.cs#L331-L352)).
  - "Cubrir", para una FK no única, es que las columnas del otro índice **empiecen** por las de la FK: `coveringIndexProperties.Select(p => p.Name).StartsWith(properties.Select(p => p.Name))` ([`:369-374`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/Metadata/Conventions/ForeignKeyIndexConvention.cs#L369-L374)).
  - Cuando se agrega un índice, la convención quita los índices que ese índice cubre (`ProcessIndexAdded`, [`:214-223`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/Metadata/Conventions/ForeignKeyIndexConvention.cs#L214-L223)). Solo puede quitar los que creó la convención: un `HasIndex` explícito tiene más prioridad y se queda (variante C del experimento EF). [INFERENCIA sobre la prioridad, confirmada por el experimento]
- Experimento EF, cinco variantes sobre la migración actual [EXPERIMENTO]:

  | Variante en `TaskItemConfiguration.cs` | Migración generada |
  |---|---|
  | A. Reemplazar `HasIndex(t => t.OwnerId)` por el compuesto | `DropIndex IX_TaskItems_OwnerId` + `CreateIndex IX_TaskItems_OwnerId_CreatedAt_Id` |
  | B. Borrar `HasIndex(t => t.OwnerId)` sin agregar nada | **Vacía**: la convención de FK mantiene `IX_TaskItems_OwnerId` |
  | C. Dejar el simple y agregar el compuesto | Solo `CreateIndex` del compuesto: quedan los dos |
  | D. Compuesto con `IsDescending(false, true, true)` e `IncludeProperties(...)` | `CreateIndex ... descending: [false, true, true]` + anotación `Npgsql:IndexInclude` |
  | E. Compuesto con `IsCreatedConcurrently()` | `DROP INDEX` en una transacción, `COMMIT`, y después `CREATE INDEX CONCURRENTLY` |

- El repo ya tenía un ejemplo de la convención: `IdempotencyKeys` tiene FK en `OwnerId` y clave primaria `(OwnerId, Key)` (`IdempotencyKeyConfiguration.cs:10`), y no tiene `IX_IdempotencyKeys_OwnerId`. La cascada desde `UserProfiles` usa `PK_IdempotencyKeys` (salió en el mismo log de `auto_explain`). [EXPERIMENTO]

## 3. Cómo verificar el plan en un test

**Respuesta:** interceptor de comandos para capturar el SQL real, `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` con los mismos parámetros en otra conexión, y aserciones sobre el árbol JSON. Datos de volumen moderado en una base propia, con `ANALYZE`.

**Capturar el SQL real: interceptor, no `ToQueryString`**

| Forma | Qué da | Problema |
|---|---|---|
| `ToQueryString()` | Texto con los parámetros como comentarios (`-- @who='u1'`) | "This string may not be suitable for direct execution and is intended only for use in debugging" ([API, ToQueryString](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toquerystring)). Además necesita el `IQueryable`, que vive dentro de `ListTasks.ExecuteAsync` y no sale. |
| `CreateDbCommand()` | Un `DbCommand` con parámetros | Advierte "there is no guarantee that executing this command directly will result in the same behavior as if EF Core had executed the command" ([API, CreateDbCommand](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationalqueryableextensions.createdbcommand)). También necesita el `IQueryable`. |
| `DbCommandInterceptor` | El comando exacto que EF manda, con sus parámetros, desde la slice real | Ninguno para este uso. "`DbCommandInterceptor.ReaderExecuting` is called before a query is executed" ([EF Core, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)). |

- El experimento con `ToQueryString` dio `-- @who='u1'` / `-- @p='11'` antes del `SELECT`. [EXPERIMENTO]
- El repo ya usa este patrón: `IdentityCommandCounter` es un `DbCommandInterceptor` que guarda el texto de cada comando (`backend/Tests/Support/IdentityCommandCounter.cs:7-33`), y `PermissionMemoTests` lo registra con `services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(counter))`. Para el plan hace falta guardar también los parámetros: `command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone())`. [EXPERIMENTO]
- Un interceptor registrado así es una instancia compartida: "Because these interceptors are singletons, they must be thread-safe" ([EF Core, Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)). `IdentityCommandCounter` ya usa `ConcurrentQueue`.
- SQL que capturó el interceptor desde la slice real [EXPERIMENTO]:

  ```sql
  SELECT t."Id", t."Title", t."IsCompleted", t."CreatedAt", t."UpdatedAt", t.xmin
  FROM "TaskItems" AS t
  WHERE t."OwnerId" = @callerId AND (t."CreatedAt", t."Id") < (@after_CreatedAt, @after_Id)
  ORDER BY t."CreatedAt" DESC, t."Id" DESC
  LIMIT @p
  ```

  Parámetros: `@callerId` (Varchar), `@after_CreatedAt` (TimestampTz), `@after_Id` (Uuid), `@p` (Integer, 11). La primera página es igual sin la comparación de filas.
- Cómo encontrar el comando: durante un `GET /api/v1/tasks` el único comando que lee `FROM "TaskItems"` es el listado (los permisos leen tablas `AspNet*`). Alcanza con filtrar por ese texto y afirmar que hay exactamente uno por pedido. `TagWith("ListTasks")` lo haría más explícito ([EF Core, Query Tags](https://learn.microsoft.com/en-us/ef/core/querying/tags)), pero cambia `ListTasks.cs`, que el plan no nombra. [INFERENCIA]

**`EXPLAIN` con parámetros vinculados funciona**

- Npgsql manda los parámetros por el protocolo extendido: el texto lleva `$1`, `$2`, y los valores van aparte. "Query planning typically occurs when the Bind message is processed" ([PostgreSQL 18, Extended Query](https://www.postgresql.org/docs/18/protocol-flow.html#PROTOCOL-FLOW-EXT-QUERY)).
- Experimento PG: `new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sqlCapturado)` con los parámetros clonados devolvió el plan. Las condiciones del plan muestran los valores reales (`'u1'::text`, `'2026-11-15 06:02:44.334859+00'::timestamp with time zone`), o sea que es un plan custom. [EXPERIMENTO]
- `ANALYZE` ejecuta la consulta: "The ANALYZE option causes the statement to be actually executed, not only planned" ([PostgreSQL 18, EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html)). Para un `SELECT` no hay efectos.
- En PostgreSQL 18 `BUFFERS` viene solo con `ANALYZE`: "Buffers information is automatically included when ANALYZE is used" ([PostgreSQL 18, EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html)). Igual conviene escribirlo, por claridad.

**Plan custom frente a plan genérico**

- Un plan genérico sirve para cualquier valor de los parámetros; uno custom usa los valores de esa ejecución. Con sentencias preparadas, "the first five executions are done with custom plans", y después PostgreSQL puede pasar al genérico ([PostgreSQL 18, PREPARE](https://www.postgresql.org/docs/18/sql-prepare.html)). `plan_cache_mode` lo controla: `auto` (por defecto), `force_custom_plan`, `force_generic_plan` ([PostgreSQL 18, Query Planning](https://www.postgresql.org/docs/18/runtime-config-query.html)).
- Npgsql no prepara sola por defecto: `Max Auto Prepare` vale 0 y "Zero disables automatic preparation" ([Npgsql, Connection String Parameters](https://www.npgsql.org/doc/connection-string-parameters.html); [Npgsql, Prepared Statements](https://www.npgsql.org/doc/prepare.html)). El repo no lo configura (grep de `Auto Prepare` sin resultados). Entonces cada consulta del listado se planifica con sus valores: el test con plan custom refleja lo que pasa en la app. [INFERENCIA]
- Si algún día se prende la preparación automática, conviene mirar también el genérico. PostgreSQL 16+ tiene `EXPLAIN (GENERIC_PLAN)`, que acepta `$1` sin valores y no se combina con `ANALYZE` ([PostgreSQL 18, EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html)). En el experimento PG el genérico también usó el índice: `Index Only Scan Backward using "IX_TaskItems_OwnerId_CreatedAt_Id"`, `Index Cond: (("OwnerId" = $1) AND (ROW("CreatedAt", "Id") < ROW($2, $3)))`. [EXPERIMENTO; no lo recomiendo como aserción del paso]

**Qué afirmar**

1. Hay un nodo `Index Scan` con `"Index Name": "IX_TaskItems_OwnerId_CreatedAt_Id"` y `"Scan Direction": "Backward"`.
2. No hay ningún nodo cuyo tipo termine en `Sort` (cubre `Sort` e `Incremental Sort`).
3. Cada nodo tiene `Actual Rows × Actual Loops ≤ pageSize + 1`.
4. En la página con cursor, la comparación `ROW(...)` aparece en el `Index Cond`, no en un `Filter`.

- La aserción 1 es la que pide el plan. Las 2 y 3 son el "porqué" medido: sin ellas, un `Bitmap Index Scan` sobre el mismo índice seguido de un `Sort` pasaría la aserción 1. El experimento PG lo vio con 50 dueños × 40 tareas: la página con cursor eligió `Bitmap Heap Scan` + `Sort` sobre el índice compuesto. [EXPERIMENTO]
- Un tope de buffers (por ejemplo `≤ 2 × (pageSize + 1)`) solo discrimina si las filas del dueño están **mezcladas** en la tabla. Si el test inserta las tareas del usuario todas juntas, ocupan pocas páginas contiguas, y sin el índice la consulta toca solo 16 buffers: pasa el tope igual (experimento test, sección 7). Para que el tope mida algo, la carga tiene que mezclar las filas (`ORDER BY random()` en el `INSERT ... SELECT`). [EXPERIMENTO]
- El tiempo (`Execution Time`) no sirve como aserción: depende de la máquina. Va a la salida del test como dato, no al `Assert`. [INFERENCIA]

**Por qué con tablas chicas sale `Seq Scan`, y qué datos hacen falta**

- PostgreSQL lo dice: "It is especially fatal to use very small test data sets ... the 100 rows probably fit within a single disk page, and there is no plan that can beat sequentially fetching 1 disk page" ([PostgreSQL 18, Examining Index Usage](https://www.postgresql.org/docs/18/indexes-examine.html)).
- Barrido del experimento PG, con el índice compuesto y `ANALYZE` [EXPERIMENTO]:

  | Dueños × tareas | Filas | Primera página | Página con cursor |
  |---|---|---|---|
  | 1 × 15 | 15 | `Seq Scan` + `Sort` | igual |
  | 10 × 20 | 200 | `Seq Scan` + `Sort` | igual |
  | 1 × 100 | 100 | `Index Scan Backward` | `Index Scan Backward` |
  | 50 × 40 | 2 000 | `Index Scan Backward` | **`Bitmap Heap Scan` + `Sort`** |
  | 10 × 200, 20 × 200, 5 × 500, 20 × 500, 10 × 1 000 | 2 000 a 10 000 | `Index Scan Backward` (3 de 3 corridas) | `Index Scan Backward` (3 de 3) |

- Lectura: el plan estable aparece cuando **el dueño del test tiene cientos de tareas** y la tabla tiene varios miles repartidas en varios dueños. Con pocas tareas por dueño, una página con cursor deja pocas filas candidatas y el planner prefiere juntarlas y ordenarlas; eso es correcto de su parte. [INFERENCIA, a partir del barrido]
- Sin el índice compuesto, con 10 × 200 y 20 × 500, las dos páginas salieron con `Sort` (con `IX_TaskItems_OwnerId` o sin ningún índice). El test falla, como tiene que pasar. [EXPERIMENTO]
- **`ANALYZE` primero.** "Examining an application's index usage without having run ANALYZE is therefore a lost cause" ([PostgreSQL 18, Examining Index Usage](https://www.postgresql.org/docs/18/indexes-examine.html)). En el barrido, sin `ANALYZE` salieron los mismos planes, porque `CREATE INDEX` actualiza `reltuples` y `relpages` ([PostgreSQL 18, Statistics Used by the Planner](https://www.postgresql.org/docs/18/planner-stats.html)). Igual conviene correrlo: en el test el índice existe **antes** de cargar los datos (lo crea la migración), así que nada actualiza esas cifras salvo `ANALYZE` o el autovacuum. [EXPERIMENTO + INFERENCIA]

**`enable_seqscan = off`: no**

- PostgreSQL lo describe como herramienta de diagnóstico: "When indexes are not used, it can be useful for testing to force their use" ([PostgreSQL 18, Examining Index Usage](https://www.postgresql.org/docs/18/indexes-examine.html)), y "These configuration parameters provide a crude method of influencing the query plans" ([PostgreSQL 18, Planner Method Configuration](https://www.postgresql.org/docs/18/runtime-config-query.html)).
- Además no prohíbe: "It is impossible to suppress sequential scans entirely, but turning this variable off discourages the planner" (igual para `enable_sort`) ([misma página](https://www.postgresql.org/docs/18/runtime-config-query.html)).
- Pros: el test no depende del volumen; pocas filas alcanzan.
- Contras: prueba que el índice **puede** servir la consulta, no que el planner lo **elige** con datos razonables. El concepto del paso es "medido, no supuesto": forzar el plan es suponer. [INFERENCIA]
- Recomiendo datos de volumen moderado y sin `enable_*`. Si un día el test resulta inestable en CI, `SET enable_sort = off` en la conexión del `EXPLAIN` es la palanca menos invasiva, y el cierre del paso tendría que decirlo. [INFERENCIA]

**Cómo evitar un test frágil**

- **Base propia.** `TestDatabases.CreateEmptyAsync` crea una base nueva en el mismo servidor (`backend/Tests/Support/TestDatabases.cs:13-24`), y la API la migra al arrancar. Las estadísticas de esa tabla no dependen de lo que hagan otros tests en paralelo. `ApiFixture.CreateIsolatedClient(settings, configureServices, out _)` acepta `ConnectionStrings:DefaultConnection` como setting y lo pisa (`ApiFixture.cs:36-49`, `:70-74`). [INFERENCIA, a partir del código]
- **Carga por SQL**, no por HTTP: un `INSERT ... SELECT generate_series` con usuarios en `AspNetUsers` y `UserProfiles` (por la FK) y tareas mezcladas. Miles de filas tardan milisegundos. [EXPERIMENTO]
- **Autovacuum.** Puede correr un `ANALYZE` propio cuando cambian más de 50 filas + 10 % de la tabla ([PostgreSQL 18, Autovacuum](https://www.postgresql.org/docs/18/runtime-config-autovacuum.html); [Routine Vacuuming](https://www.postgresql.org/docs/18/routine-vacuuming.html)). Sobre los mismos datos llega a las mismas estadísticas; no cambia el plan. [INFERENCIA]
- **Paralelismo y JIT.** Un scan paralelo solo se considera desde `min_parallel_table_scan_size` = 8 MB, y el JIT desde un costo de 100 000 ([PostgreSQL 18, Query Planning](https://www.postgresql.org/docs/18/runtime-config-query.html)). La tabla del test ocupa 1,6 MB con 21 000 filas y la consulta cuesta muy poco: ninguno de los dos entra. [EXPERIMENTO para el tamaño; INFERENCIA para el efecto]
- **Muestreo de `ANALYZE`.** Con `default_statistics_target` = 100, `ANALYZE` lee una muestra ([misma página](https://www.postgresql.org/docs/18/runtime-config-query.html)). Con pocos miles de filas lee toda la tabla, así que la estadística no varía entre corridas. [INFERENCIA]
- **Página con cursor cerca del principio.** Un cursor cerca del final de la lista deja pocas filas, y el planner puede elegir `Bitmap` + `Sort` con razón. El test pide la segunda página, no la última. [INFERENCIA, a partir del barrido]

## 4. "Medido": qué evidencia de antes y después

**Respuesta:** filas leídas, buffers, presencia de `Sort` y tiempo, de `EXPLAIN (ANALYZE, BUFFERS)` sobre la misma consulta y los mismos datos, antes y después de la migración. Se guarda en `artifacts/verify-template/<runId>/` y el resumen va a `progreso.md`.

**Medición del experimento PG** (100 dueños × 2 000 tareas = 200 000 filas mezcladas, `ANALYZE`, consulta capturada de `ListTasks` con `pageSize` = 10) [EXPERIMENTO]:

| Índices | Página | Plan | Filas leídas | Buffers | Tiempo |
|---|---|---|---|---|---|
| solo `IX_TaskItems_OwnerId` (hoy) | primera | `Bitmap Heap Scan` + top-N `Sort` | 2 000 | 1 189 | 1,05 ms |
| solo `IX_TaskItems_OwnerId` (hoy) | con cursor | `Bitmap Heap Scan` + `Filter` + top-N `Sort` | 2 000 (descarta 10) | 1 189 | 0,81 ms |
| solo compuesto | primera | `Index Scan Backward` | 11 | 10 | 0,021 ms |
| solo compuesto | con cursor | `Index Scan Backward`, `ROW` en `Index Cond` | 11 | 11 | 0,026 ms |
| los dos | primera / cursor | igual que solo compuesto | 11 | 10 / 11 | 0,019 / 0,022 ms |

- Lectura: el trabajo sin índice crece con las tareas del dueño (2 000 filas y ~1 200 bloques para mostrar 10). Con el índice queda fijo en una página más una fila. Los tiempos son chicos en las dos columnas porque todo estaba en memoria (`read=0`); con datos en disco la diferencia de buffers se vuelve diferencia de lecturas. [INFERENCIA]
- Mismo patrón con datos agrupados por dueño (sin mezclar): 20 buffers sin índice frente a 14-15 con índice. Eso muestra que los buffers dependen de cómo están las filas en disco. Las filas leídas y el `Sort` no dependen de eso. [EXPERIMENTO]

**Cómo registrarla sin herramientas nuevas**

1. **Antes:** sobre la base del paso con la migración anterior, la carga del test y `ANALYZE`, correr el `EXPLAIN (ANALYZE, BUFFERS)` de la consulta capturada y guardar la salida. Lo más simple es correr el test nuevo **sin** la migración: falla, y el mensaje trae el plan. Eso es a la vez el "rojo" del test y la medición de antes. [INFERENCIA]
2. **Después:** el mismo test en verde. Que el test imprima el plan (`TestContext.Current.TestOutputHelper`) y que la corrida guarde la salida en `artifacts/verify-template/<runId>/`. [INFERENCIA]
3. **Tamaño y costo de escritura:** `pg_relation_size` de cada índice, desde `pg_stat_user_indexes` ([PostgreSQL 18, Cumulative Statistics](https://www.postgresql.org/docs/18/monitoring-stats.html)). La misma vista da `idx_scan` ("Number of index scans initiated on this index"), útil para comprobar en un entorno real que el índice viejo ya no se usaba. [INFERENCIA]
4. El resumen (tabla de antes y después) va a la entrada del paso en `progreso.md`, como pide `AGENTS.md`.

**Herramientas que existen** (ninguna hace falta para este paso)

| Herramienta | Qué hace | Licencia | Qué pide en este repo |
|---|---|---|---|
| `EXPLAIN (ANALYZE, BUFFERS)` | El plan real de una consulta | Parte de PostgreSQL ([licencia PostgreSQL](https://www.postgresql.org/about/licence/)) | Nada. **Es lo que recomiendo.** |
| `auto_explain` | Escribe en el log el plan de las consultas lentas, también las anidadas (`log_nested_statements`) ([PostgreSQL 18, auto_explain](https://www.postgresql.org/docs/18/auto-explain.html)) | Módulo de PostgreSQL (misma licencia) | Viene en la imagen `postgres:18` (`auto_explain.so`, experimento). `LOAD 'auto_explain'` por sesión exige superusuario. Lo usé para medir la cascada. |
| `pg_stat_statements` | Estadísticas acumuladas de cada consulta del servidor ([PostgreSQL 18, pg_stat_statements](https://www.postgresql.org/docs/18/pgstatstatements.html)) | Módulo de PostgreSQL (misma licencia) | Viene en la imagen, pero "must be loaded by adding pg_stat_statements to shared_preload_libraries", con reinicio y `CREATE EXTENSION`. Cambia `compose.yaml`. Sirve para operar, no para este test. |
| HypoPG | Índices hipotéticos para ver el plan sin crearlos ([repo](https://github.com/HypoPG/hypopg)) | Licencia tipo PostgreSQL ([LICENSE](https://github.com/HypoPG/hypopg/blob/master/LICENSE)) | **No** viene en `postgres:18` (experimento: no está en `/usr/share/postgresql/18/extension`). Pide otra imagen o compilarla: decisión de herramienta (regla 5). No hace falta: crear el índice real en una base de test cuesta milisegundos. |

## 5. Cómo encaja en la arquitectura del repo

- **Dónde vive el índice:** `Features/Tasks/Persistence/TaskItemConfiguration.cs`. `Persistence/` "contiene los mappings de EF" (`docs/conventions.md`, sección "Dónde va cada cosa"). La migración va a `Infrastructure/Persistence/Migrations/`, generada por `dotnet ef migrations add`, como las anteriores.
- **Dónde vive el test:** al lado de la slice, en `Features/Tasks/ListTasks/`, porque prueba la consulta de esa slice (ADR-0004; `conventions.md`: "Adentro, un archivo por pieza: ... `CreateTaskTests.cs`"). La convención nombra un solo archivo de tests por slice. Dos caminos:
  - Un `[Fact]` más en `ListTasksTests.cs`, que ya prueba la paginación. Gana: respeta "un archivo de tests por slice". Pierde: el constructor suma `PostgresFixture` y el archivo crece con la carga por SQL y los helpers del plan.
  - Una clase `ListTasksPlanTests` en su archivo. Gana: separa un test de rendimiento, con otra base y otro ritmo, de los tests de contrato. Pierde: un segundo archivo de tests en la slice, algo que la convención no menciona. El proyecto de tests lo compila igual (`TemplateApp.Tests.csproj` incluye `..\Features\**\*Tests.cs`), y el sufijo `*Tests` pasa DUNE0008.
  - Recomiendo la clase aparte, porque "un archivo, un tipo" ya obliga a un archivo por clase y el test necesita un fixture distinto. Va como decisión en `docs/decisions.tsv`. [INFERENCIA]
- **Helpers en `Tests/Support/`:** dos piezas que no son de la slice y que otro paso puede reusar (por ejemplo, la consulta de permisos de `progreso.md:146`):
  - Un interceptor que guarda texto **y** parámetros de cada comando. Puede ser una clase nueva o una generalización de `IdentityCommandCounter`. Recomiendo una nueva, para no tocar `PermissionMemoTests` (regla 7). [INFERENCIA]
  - Un lector de planes: corre `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` y aplana el árbol en nodos con tipo, índice, dirección, `Index Cond`, filas reales y buffers. Con un tipo por archivo (DUNE0010). [INFERENCIA]
- **¿Regla nueva en `conventions.md`?** No hace falta una regla general. [INFERENCIA]
  - La regla 4 de `AGENTS.md` pide que "Toda regla nueva se hace ejecutable en Dune". Una regla como "toda lista paginada tiene un test de plan" pediría un gate que reconozca listas keyset (por ejemplo, un chequeo en `check-structure.mjs`) con fixture válida y violación. Hoy hay **una** lista. El gate costaría más que lo que protege.
  - El test ya es la verificación ejecutable del índice concreto: si alguien borra el índice o cambia el orden de la consulta, falla.
  - Sí conviene **documentar el hecho** al lado de la regla de keyset que ya existe en `conventions.md` (qué índice sirve a la lista y qué test lo cuida), redactado como descripción y no como regla nueva. Si Tobi prefiere que sea regla, entonces aplica la regla 4 y el paso crece.
- **Paso 19 y producción:** el orden "crear el nuevo, borrar el viejo" y `CONCURRENTLY` son del paso 19 (sección 1). Este paso usa la migración que genera EF. [INFERENCIA]

## 6. Recomendación para este repo

1. `TaskItemConfiguration.cs`: reemplazar `builder.HasIndex(t => t.OwnerId);` por `builder.HasIndex(t => new { t.OwnerId, t.CreatedAt, t.Id });`. Sin `IsDescending`, sin `IncludeProperties`, sin `IsCreatedConcurrently`.
2. `dotnet ef migrations add AddTaskListIndex` (o el nombre que elija el paso) y no editarla: `DropIndex IX_TaskItems_OwnerId` + `CreateIndex IX_TaskItems_OwnerId_CreatedAt_Id`.
3. Test `ListTasksPlanTests` en `Features/Tasks/ListTasks/`:
   - Base propia con `TestDatabases.CreateEmptyAsync`, cliente aislado con la cadena de esa base y el interceptor de comandos.
   - Registrar un usuario por HTTP y cargar por SQL unos 200 dueños × 100 tareas más ~1 000 tareas del usuario, **mezcladas** (`ORDER BY random()`), y `ANALYZE "TaskItems"`.
   - Pedir la primera página y la segunda (con cursor) por HTTP.
   - Por cada comando que lee `FROM "TaskItems"`: `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` con los mismos parámetros, y afirmar `Index Scan` + `IX_TaskItems_OwnerId_CreatedAt_Id` + `Backward`, ningún `*Sort`, filas ≤ `pageSize + 1`, y `ROW(` en el `Index Cond` de la página con cursor.
   - Imprimir el plan en la salida y en cada mensaje de falla.
4. Evidencia: el test en rojo antes de la migración (con el plan de antes en el mensaje), en verde después, y la tabla de antes y después en `progreso.md`. Sin herramientas, paquetes ni imágenes nuevas.

## 7. Contraste con el borrador que hay en el worktree

Mientras investigaba aparecieron en el worktree cambios sin commitear que no hice yo: `TaskItemConfiguration.cs` con el índice compuesto, la migración `20261117132830_AddTaskListIndex`, el snapshot, `ListTasksPlanTests.cs`, cuatro archivos en `Tests/Support/` (`CapturedCommand`, `CapturedCommands`, `QueryPlan`, `QueryPlanNode`) y una línea nueva en `docs/conventions.md`. No los toqué. Los copié a la carpeta temporal y los corrí contra el contenedor del experimento.

- **Con el índice:** PASS en 4 de 4 corridas, cada una con base nueva. [EXPERIMENTO]
- **Sin el índice** (configuración y snapshot de `HEAD`, sin la migración nueva): FAIL con el mensaje "The list must walk IX_TaskItems_OwnerId_CreatedAt_Id backward. Plan: Limit rows=11 buffers=16 -> Sort rows=11 buffers=16 -> Bitmap Heap Scan rows=1000 buffers=16 -> Bitmap Index Scan using IX_TaskItems_OwnerId rows=1000 buffers=2". La violación deliberada se detecta. [EXPERIMENTO]
- **Coincide** con esta investigación en la forma del índice, el borrado del simple, el interceptor con parámetros clonados, `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)`, la base propia, la carga por SQL con `ANALYZE` y las aserciones de índice, dirección, `Sort`, filas y `ROW(` en la página con cursor.
- **Diferencia 1, el tope de buffers:** el borrador afirma `≤ 2 × (pageSize + 1)` = 22 bloques. Su carga inserta las 1 000 tareas del usuario juntas, después de las de los demás. En la base que dejó la corrida, esas filas ocupan las páginas 186 a 199 del heap. Sin el índice la consulta tocó 16 buffers: **el tope pasa igual sin índice**. El test falla por las otras aserciones, así que hoy no es un falso verde. Pero el mensaje de `conventions.md` presenta el tope como una de las tres cosas que el test detecta. Para que lo sea, la carga tiene que mezclar las filas (`ORDER BY random()`); con filas mezcladas, sin índice, el experimento PG midió 1 189 buffers. [EXPERIMENTO]
- **Diferencia 2, la línea en `conventions.md`:** está escrita como regla ("La lista tiene un índice ... `ListTasksPlanTests` lo mide"). Si se lee como regla nueva, la regla 4 pide Dune. Recomiendo redactarla como descripción del hecho o anotar en `decisions.tsv` que la verificación ejecutable es el test (sección 5).
- **Diferencia 3, el archivo de tests:** la clase vive en su propio archivo, junto a `ListTasksTests.cs`. Coincide con mi recomendación, pero conviene una línea en `decisions.tsv`, porque la convención nombra un solo archivo de tests por slice (sección 5).

## Decisiones abiertas

Ninguna es de herramienta, librería o imagen: todo usa EF Core, Npgsql, PostgreSQL y xUnit, que ya están. En modo autónomo las toma el agente con la recomendación y las anota.

1. **Borrar `IX_TaskItems_OwnerId`.**
   - Borrarlo: ganás menos costo de escritura y espacio. No perdés lecturas: la cascada y el filtro por dueño usan el compuesto (medido).
   - Dejarlo: no ganás nada medible y pagás un índice más en cada `INSERT`.
   - Recomiendo **borrarlo**, en la misma migración que genera EF.
2. **Dónde vive el test:** clase aparte `ListTasksPlanTests` (recomendada) o un `[Fact]` más en `ListTasksTests`. Ver sección 5.
3. **Regla nueva o descripción en `conventions.md`:** recomiendo **descripción**, y que la verificación ejecutable sea el test. Si Tobi quiere una regla general, entra la regla 4 y hace falta un gate de Dune.
4. **Tope de buffers en el test:** mezclar las filas en la carga para que el tope signifique algo (recomendado), o sacarlo y quedarse con filas y `Sort`.

## Pendientes que encontré fuera del alcance (regla 7)

- Orden seguro de una migración de índice en producción (crear el nuevo antes de borrar el viejo, `CONCURRENTLY`): encaja con el paso 19.
- `CreateEmptyAsync` deja las bases `startup_*` creadas en el servidor de tests. Con Testcontainers se borran con el contenedor; con `TEST_POSTGRES_CONNECTION` quedan. No afecta a este paso.
- `progreso.md:146` propone la consulta de permisos de `AccessPolicy` "encaja con el paso 18". Es otro concepto (forma de la consulta, no índice); el paso no debería absorberlo.

## Qué quedó sin comprobar

- No corrí la suite completa de backend con el test nuevo ni `check-all.mjs`. Corrí solo la clase `ListTasksPlanTests` del borrador, contra un contenedor propio, sin Testcontainers y sin los analyzers de Dune (la copia no tenía `Directory.Build.props`).
- No probé el test en CI (Linux, GitHub Actions). La estabilidad del plan en otra máquina es una inferencia: depende de las estadísticas, y esas no dependen de la máquina.
- La medición del costo de escritura es gruesa: una corrida por variante y la tabla crecía entre corridas.
- No medí con datos en disco (`read` > 0); todos los planes tenían los bloques en memoria.
- No leí la parte del código de EF Core que decide la prioridad de configuración (explícita sobre convención). La afirmación sale del experimento (variante C), no del código.
- No verifiqué cómo nombra EF el índice cuando la tabla y la entidad tienen nombres distintos; acá coinciden en la parte que importa (`IX_TaskItems_...`, experimento).
- La herramienta `dotnet-ef` instalada es 10.0.2 y el runtime 10.0.12; avisó la diferencia pero generó las migraciones. El paso debería usar la misma herramienta que usaron las migraciones anteriores.

## Fuentes

- https://www.postgresql.org/docs/18/indexes-multicolumn.html
- https://www.postgresql.org/docs/18/indexes-ordering.html
- https://www.postgresql.org/docs/18/indexes-index-only-scans.html
- https://www.postgresql.org/docs/18/indexes-examine.html
- https://www.postgresql.org/docs/18/sql-createindex.html
- https://www.postgresql.org/docs/18/ddl-constraints.html#DDL-CONSTRAINTS-FK
- https://www.postgresql.org/docs/18/btree.html#BTREE-DEDUPLICATION
- https://www.postgresql.org/docs/18/sql-explain.html
- https://www.postgresql.org/docs/18/sql-prepare.html
- https://www.postgresql.org/docs/18/runtime-config-query.html
- https://www.postgresql.org/docs/18/runtime-config-autovacuum.html
- https://www.postgresql.org/docs/18/routine-vacuuming.html
- https://www.postgresql.org/docs/18/planner-stats.html
- https://www.postgresql.org/docs/18/protocol-flow.html#PROTOCOL-FLOW-EXT-QUERY
- https://www.postgresql.org/docs/18/monitoring-stats.html
- https://www.postgresql.org/docs/18/auto-explain.html
- https://www.postgresql.org/docs/18/pgstatstatements.html
- https://www.postgresql.org/about/licence/
- https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore/Metadata/Conventions/ForeignKeyIndexConvention.cs
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toquerystring
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.relationalqueryableextensions.createdbcommand
- https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors
- https://learn.microsoft.com/en-us/ef/core/querying/tags
- https://www.npgsql.org/efcore/modeling/indexes.html
- https://www.npgsql.org/doc/prepare.html
- https://www.npgsql.org/doc/connection-string-parameters.html
- https://use-the-index-luke.com/sql/where-clause/the-equals-operator/concatenated-keys
- https://use-the-index-luke.com/sql/sorting-grouping/indexed-order-by
- https://use-the-index-luke.com/sql/sorting-grouping/order-by-asc-desc-nulls-last
- https://use-the-index-luke.com/sql/dml/insert
- https://github.com/HypoPG/hypopg
- https://github.com/HypoPG/hypopg/blob/master/LICENSE
