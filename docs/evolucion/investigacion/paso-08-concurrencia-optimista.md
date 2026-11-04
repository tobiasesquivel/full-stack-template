# Paso 8: concurrencia optimista en `TaskItem` (investigación)

- Fecha: 2026-11-04.
- Alcance: el paso 8 del [plan](../plan.md) (`plan.md:299-302`). Una actualización de tarea deja de pisar a ciegas lo que escribió otro cliente. Si el cliente manda la versión que leyó, cambia el contrato HTTP de `PUT /api/v1/tasks/{id}` y de `TaskResponse`, así que rige la regla 3 de `AGENTS.md`: backend, frontend y tests juntos.
- Decisiones que ya están tomadas y que este informe no reabre: EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:35`), PostgreSQL 18, ErrorOr con catálogo de errores y DUNE0014 (`docs/conventions.md:76`), `ApiResults` como único mapeo de `Error` a HTTP (`docs/conventions.md:74`), el `title` con la descripción del error (`docs/decisions.tsv:124`), la paginación por keyset del paso 7 (`docs/conventions.md:73`) y la forma de ADR-0004.
- Método: solo fuentes primarias. Son la documentación de EF Core y ASP.NET Core en Microsoft Learn, la de Npgsql y su código en la etiqueta `v10.0.3`, la de PostgreSQL, [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html), [RFC 6585](https://www.rfc-editor.org/rfc/rfc6585.html), [Google AIP-154](https://google.aip.dev/154), las [Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md) y angular.dev.
- Evidencia propia: un experimento descartable fuera del repo.
  - "Experimento EF": una consola en `/tmp/occ-exp` con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Microsoft.EntityFrameworkCore.Design` 10.0.12 y `dotnet-ef` 10.0.2 (la herramienta global instalada), contra un contenedor `postgres:18` descartable. Una tabla `TaskItems` con `Id`, `Title` e `IsCompleted`, dos `DbContext` que leen la misma fila y escriben uno después del otro, y la misma prueba con `uint Version` + `IsRowVersion()`.
  - No se tocó código del repo.

## Glosario corto

- **Lost update** (actualización perdida): dos clientes leen la misma tarea, cada uno cambia algo y el segundo en guardar borra sin saberlo el cambio del primero.
- **Concurrencia optimista**: no se bloquea nada al leer. Al guardar se comprueba que la fila no cambió desde la lectura; si cambió, el guardado falla y el cliente decide. Lo contrario es la concurrencia pesimista, que bloquea la fila al leer.
- **Token de concurrencia** (= número de versión de la fila): un valor que cambia en cada escritura. EF Core lo agrega al `WHERE` del `UPDATE`.
- **`xmin`**: una columna de sistema que PostgreSQL tiene en toda tabla, sin declararla. Guarda el id de la transacción que escribió esa versión de la fila.
- **ETag** (entity tag): un header de respuesta HTTP con un identificador opaco de la versión del recurso. **`If-Match`**: el header de pedido que dice "aplicá esto solo si la versión actual es esta".

## Conclusión

- **El problema es real y se reproduce sin carrera.** `UpdateTask` carga la tarea, pisa `Title` e `IsCompleted` con lo que mandó el cliente y guarda (`backend/Features/Tasks/UpdateTask/UpdateTask.cs:32-44`). Si dos pestañas muestran la misma tarea, una la renombra y la otra marca el checkbox, el checkbox manda el título viejo (`frontend/src/app/features/tasks/tasks/tasks.ts:104-108`) y el renombre se pierde. No hace falta que los pedidos lleguen a la vez: alcanza con que el segundo se base en una lectura vieja.
- **Token:** `xmin` con una propiedad `uint Version` e `IsRowVersion()`. Npgsql la mapea sola a `xmin`, la base la cambia en cada escritura y no hay columna nueva. El experimento EF lo confirmó: el `UPDATE` sale con `WHERE "Id" = @p1 AND xmin = @p2 RETURNING xmin`.
- **Migración:** `dotnet ef migrations add` genera un archivo con `AddColumn<uint>("xmin", type: "xid", rowVersion: true)`, pero el SQL que produce es solo la fila de `__EFMigrationsHistory`. Npgsql omite las columnas de sistema. Entonces la migración se genera por CLI sin editarla a mano, como pide el plan, pero no cambia el esquema.
- **Contrato:** recomiendo un campo `version` en `TaskResponse` y en `UpdateTaskRequest`, y **409 Conflict** con el código `Task.Conflict`. Es lo que pide el plan, es lo que describe RFC 9110 para una versión que viaja en el cuerpo, y es lo que hace Google (AIP-154). La alternativa estándar de HTTP, ETag con `If-Match`, responde **412**, no 409. Más abajo están las dos.
- **Por qué el cuerpo y no el header:** la lista del paso 7 devuelve diez tareas en una respuesta, y la UI edita cada fila desde la lista. Un ETag describe una sola respuesta; no puede llevar la versión de diez filas. Con ETag, el frontend igual necesitaría la versión de cada fila en el cuerpo, o un `GET` por fila antes de editar.
- **Versión ausente:** recomiendo exigirla (400 con `errors.Version`). Aceptar el pedido sin versión es "gana la última escritura" (last-write-wins), que es justo el defecto que el paso arregla. Es decisión de Tobi.
- **Detección:** EF Core compara contra el valor **original** de la propiedad, no el actual. Como `UpdateTask` carga la fila de nuevo en cada pedido, hay que poner la versión del cliente como valor original: `_db.Entry(item).Property(t => t.Version).OriginalValue = request.Version`. El experimento EF lo confirmó. La excepción `DbUpdateConcurrencyException` se atrapa en la slice y se devuelve `TaskErrors.Conflict`; el endpoint no cambia.
- **Rojo real:** un test de dos `PUT` basados en la misma lectura. Hoy los dos dan 200 y el `GET` final muestra el título viejo.
- **Frontend:** `TaskResponse.version` llega con cada fila, `Tasks` lo manda en cada `PUT`. Ante un 409 con `code: "Task.Conflict"`, `Tasks` muestra un mensaje propio y recarga la página actual. El mensaje actual de 409 ("Ya existe un registro con esos datos.", `frontend/src/app/core/http-error.ts:46-47`) sería falso para este caso.
- **Choque con un test vigente:** `Update_PersistsFinalState_Idempotently` repite el mismo `PUT` y espera 200 (`backend/Features/Tasks/UpdateTask/UpdateTaskTests.cs:33-35`), y el smoke hace lo mismo (`smoke-api.mjs:211`). Con versión, repetir el mismo pedido con la versión vieja da 409. RFC 9110 permite responder 2xx si el cambio "ya fue aplicado". Es decisión de Tobi (sección 3).

## 1. Qué token usar

**Respuesta:** `xmin` mapeado a `uint Version`. Es el token que la base mantiene sola, cubre toda la fila y no pide columna nueva.

**Cómo funciona un token en EF Core**

- "the value of the concurrency token on the database is compared against the original value read by EF Core". Si nadie cambió la fila, el `UPDATE` afecta 1 fila. Si otro la cambió, "the UPDATE fails to find any matching rows and reports that zero were affected. As a result, EF Core's SaveChanges() throws a DbUpdateConcurrencyException" ([EF Core, Handling Concurrency Conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)).
- Lo mismo vale para el borrado: "EF also throws DbUpdateConcurrencyException when attempting to delete a row that has been concurrently modified" (misma fuente).

**Las tres opciones**

| Opción | Cómo se configura | Quién cambia el valor | Migración |
|---|---|---|---|
| A. `xmin` | `uint Version` + `IsRowVersion()` ([Npgsql, Concurrency Tokens](https://www.npgsql.org/efcore/modeling/concurrency.html)) | PostgreSQL, en cada escritura de la fila | Archivo generado sin DDL (ver abajo) |
| B. `int Version` propio | `IsConcurrencyToken()` ([EF Core, Application-managed concurrency tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#application-managed-concurrency-tokens)) | La app: `item.Version++` en cada slice que escribe | `AddColumn<int>` con `defaultValue: 0`: las filas existentes quedan en 0 |
| C. `Guid Version` propio | `IsConcurrencyToken()`; EF lo muestra con un `Guid` (misma fuente) | La app: `item.Version = Guid.NewGuid()` | `AddColumn<Guid>`: las filas existentes quedan con `Guid.Empty` |

**Lo que dicen las fuentes de `xmin`**

- Npgsql: `xmin` "holds the ID of the latest updating transaction. Since this value automatically gets updated every time the row is changed, it is ideal for use as a concurrency token" ([Npgsql, Concurrency Tokens](https://www.npgsql.org/efcore/modeling/concurrency.html)). El ejemplo de la página es `public uint Version { get; set; }` con `.IsRowVersion()`.
- La convención del proveedor en `v10.0.3`: una propiedad `uint` con `ValueGenerated.OnAddOrUpdate`, marcada como token y de tipo `xid`, recibe el nombre de columna `xmin` ([NpgsqlPostgresModelFinalizingConvention.cs:80-91](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Metadata/Conventions/NpgsqlPostgresModelFinalizingConvention.cs#L80-L91)). El test del proveedor afirma `"xmin"` y `"xid"` ([NpgsqlPostgresModelFinalizingConventionTest.cs](https://github.com/npgsql/efcore.pg/blob/v10.0.3/test/EFCore.PG.Tests/Metadata/Conventions/NpgsqlPostgresModelFinalizingConventionTest.cs)).
- PostgreSQL: `xmin` es "The identity (transaction ID) of the inserting transaction for this row version", y cada `UPDATE` crea una versión nueva de la fila ([PostgreSQL, System Columns](https://www.postgresql.org/docs/current/ddl-system-columns.html)).

**La migración con `xmin`**

- El generador de SQL de Npgsql no toca columnas de sistema: "they should never generate migration operations", y `AddColumn` y `DropColumn` sobre `xmin` no emiten nada ([NpgsqlMigrationsSqlGenerator.cs:2028-2039](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/NpgsqlMigrationsSqlGenerator.cs#L2028-L2039) y líneas 298 y 320). El test `Add_column_system` lo afirma: "If an xmin property is added, nothing should happen" ([MigrationsNpgsqlTest.cs:880-901](https://github.com/npgsql/efcore.pg/blob/v10.0.3/test/EFCore.PG.FunctionalTests/Migrations/MigrationsNpgsqlTest.cs#L880-L901)).
- Experimento EF: `dotnet ef migrations add AddVersion` generó `migrationBuilder.AddColumn<uint>(name: "xmin", table: "TaskItems", type: "xid", rowVersion: true, nullable: false, defaultValue: 0u)`. `dotnet ef migrations script` de esa migración produjo solo el `INSERT INTO "__EFMigrationsHistory"`.
- **En este repo:** `DatabaseInitializer` aplica las migraciones al arrancar con `MigrateAsync` dentro de un advisory lock (`backend/Infrastructure/Persistence/DatabaseInitializer.cs:18-24`). La migración nueva entra en la historia y no cambia la tabla. El snapshot (`AppDbContextModelSnapshot.cs`) sí cambia: suma la propiedad `Version`.
- La regla del repo: "Las migraciones se generan por CLI y son forward, con backfill. No se editan a mano" (`docs/conventions.md:87`). Con `xmin` no hay backfill: toda fila ya tiene `xmin`.
- Cómo se genera: `dotnet ef migrations add AddTaskVersion --project backend --output-dir Infrastructure/Persistence/Migrations`. La herramienta instalada es `dotnet-ef` 10.0.2 global, sin manifiesto local en el repo; avisa que es más vieja que el runtime 10.0.12 (lo vi en el experimento), pero funcionó. Pasar `--output-dir` explícito evita depender de dónde EF ubica la migración por defecto. [INFERENCIA: no verifiqué el directorio por defecto de EF 10]

**Cuidados de `xmin`** (lo que dicen las fuentes y lo que no)

- **Se repite a muy largo plazo.** "Transaction identifiers are also 32-bit quantities. [...] It is unwise, however, to depend on the uniqueness of transaction IDs over the long term (more than one billion transactions)" ([PostgreSQL, System Columns](https://www.postgresql.org/docs/current/ddl-system-columns.html)). Para un token de concurrencia, el riesgo es que un cliente guarde una versión durante mil millones de transacciones y que justo coincida. En una UI que se recarga, no pasa. [INFERENCIA]
- **El congelado no lo cambia.** Desde PostgreSQL 9.4, `VACUUM` congela filas con un bit, "preserving the row's original xmin" ([PostgreSQL, Routine Vacuuming](https://www.postgresql.org/docs/current/routine-vacuuming.html)).
- **Una copia nueva de la fila tiene otro `xmin`.** Restaurar un `pg_dump` vuelve a insertar las filas con transacciones nuevas, así que cambia el `xmin` de todas. [INFERENCIA, a partir de la definición de `xmin`] Efecto en este repo: una pestaña abierta durante una restauración recibe un 409 de más y recarga. Es molesto, no corrompe nada. Para `VACUUM FULL`, la documentación solo dice que escribe "a complete new version of the table file" ([Routine Vacuuming](https://www.postgresql.org/docs/current/routine-vacuuming.html)); no dice si conserva `xmin`. Sin comprobar.
- **Replicación lógica:** las filas del suscriptor tienen su propio `xmin`. Solo importa si algún día se escribe en una réplica. No encontré fuente primaria que lo diga; queda sin comprobar.
- **Cambia con cualquier escritura de la fila**, también la de un paso futuro que no sea un cambio del usuario. Hoy solo `UpdateTask` y `DeleteTask` escriben `TaskItems`. Con un token propio (B o C), la app decide qué escrituras cuentan; EF Core lo nombra como ventaja: "you can control precisely when it gets regenerated, to avoid needless concurrency conflicts" ([EF Core, Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#application-managed-concurrency-tokens)).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. `xmin` | Cero columnas, cero código para subir la versión, cubre cualquier escritura. Es lo que documenta Npgsql. | Ata el token a PostgreSQL. Valores raros para el que lee (`762`, `763`, saltos grandes). Una restauración invalida todas las versiones. La migración no cambia el esquema, así que "migración aplicada" prueba poco. |
| B. `int Version` | Valor legible (1, 2, 3), portable a otra base, migración con DDL real y backfill a 0. | Cada slice que escribe tiene que subirlo; olvidarlo en una slice nueva deja un agujero. Una columna más. |
| C. `Guid Version` | Portable, sin orden que alguien pueda interpretar. Es el ejemplo de ASP.NET Core para bases sin `rowversion` ([ASP.NET Core, Concurrency](https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/concurrency)). | Lo mismo que B, con un valor de 16 bytes y 36 caracteres en el JSON. |

Recomendación para este repo: **A**. El repo ya está atado a PostgreSQL (el paso 7 usa la comparación de filas de Npgsql), y el concepto del paso es el lost update, no cómo se genera la versión. Lo que hace la industria: en SQL Server, `rowversion`; en PostgreSQL con EF Core, `xmin`, que es lo que documenta el mantenedor. B es lo habitual cuando el modelo tiene que ser portable.

## 2. Cómo manda el cliente la versión que leyó

**Respuesta:** un campo `version` en el cuerpo, y 409 si no coincide. Con ETag + `If-Match`, el estándar es 412.

**Lo que dice RFC 9110**

- `If-Match` "is most often used with state-changing methods (e.g., POST, PUT, DELETE) to prevent accidental overwrites when multiple user agents might be acting in parallel on the same resource (i.e., to prevent the "lost update" problem)". Si la condición es falsa, el servidor "MUST NOT perform the requested method" y "MAY indicate that the conditional request failed by responding with a 412 (Precondition Failed)" ([RFC 9110 §13.1.1](https://www.rfc-editor.org/rfc/rfc9110.html#name-if-match)). Para `If-Match` se usa la comparación fuerte (misma sección).
- 412: "one or more conditions given in the request header fields evaluated to false" ([RFC 9110 §15.5.13](https://www.rfc-editor.org/rfc/rfc9110.html#name-412-precondition-failed)). Es para condiciones **en headers**.
- 409: "Conflicts are most likely to occur in response to a PUT request. For example, if versioning were being used and the representation being PUT included changes to a resource that conflict with those made by an earlier (third-party) request, the origin server might use a 409 response" ([RFC 9110 §15.5.10](https://www.rfc-editor.org/rfc/rfc9110.html#name-409-conflict)). Es el caso de una versión que viaja **en el cuerpo**.
- Un ETag es "an opaque quoted string, possibly prefixed by a weakness indicator": `ETag: "xyzzy"` ([RFC 9110 §8.8.3](https://www.rfc-editor.org/rfc/rfc9110.html#name-etag)).

**Lo que hace la industria**

- Google, AIP-154: el recurso lleva un campo `etag` en el cuerpo, que "must be provided by the server on output". Si no coincide, "the service must send an ABORTED error response", que es HTTP 409. El changelog dice que pasaron de `FAILED_PRECONDITION` (400) a `ABORTED` (409) ([AIP-154](https://google.aip.dev/154)).
- Azure: "YOU SHOULD return an ETag with any operation returning the resource", y un `PUT` con `If-Match` que no coincide da `412-Precondition Failed`. También: "DO honor any precondition headers received" ([Azure REST API Guidelines, Conditional Requests](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)).

**¿El plan choca con el estándar?** No. El plan dice "la otra recibe 409" (`plan.md:302`). Con la versión en el cuerpo, 409 es lo que describe RFC 9110 §15.5.10 y lo que usa AIP-154. Solo chocaría si se elige ETag + `If-Match`: entonces el estándar es 412 y el plan habría que corregirlo. Lo marco; no lo resuelvo.

**Las dos formas, sobre este repo**

| Opción | Cómo se ve | Qué cambia en el repo |
|---|---|---|
| A. Campo `version` + 409 | `TaskResponse` suma `version`; `PUT` manda `{ title, isCompleted, version }`; conflicto = 409 `Task.Conflict` | `TaskResponse.cs`, `TaskResponses.Projection`, `UpdateTaskRequest.cs`, su validator, `TaskErrors`; en Angular, `TaskResponse`, `UpdateTaskRequest` y `Tasks`. `ApiResults` ya mapea `Conflict` a 409 (`backend/Shared/Http/ApiResults.cs:31`). |
| B. ETag + `If-Match` + 412 | `GET /tasks/{id}` y `PUT` responden `ETag: "763"`; `PUT` manda `If-Match: "763"`; conflicto = 412 | Lo anterior, más: el endpoint lee `If-Match` (`[FromHeader(Name = "If-Match")]`, [ASP.NET Core, parameter binding](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding), o `RequestHeaders.IfMatch`, [Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.headers.requestheaders.ifmatch)) y escribe `Response.Headers.ETag` ([Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.iheaderdictionary.etag)). `ApiResults` no tiene ningún `ErrorType` que dé 412 (`ApiResults.cs:26-34`): hay que agregar el mapeo, porque es el único lugar que traduce errores a HTTP (`docs/conventions.md:74`). Angular usa `observe: 'response'` para leer el header ([angular.dev, Making requests](https://angular.dev/guide/http/making-requests)). Y la lista igual necesita la versión por fila en el cuerpo. |

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A | Un solo lugar para la versión, también en la lista. Encaja con el catálogo y con `ApiResults` sin tocarlos. Es lo que pide el plan. | No usa el mecanismo HTTP estándar; un cliente genérico (proxy, caché) no lo entiende. |
| B | El mecanismo de RFC 9110, que entienden herramientas HTTP. Abre la puerta a `If-None-Match` y 304 en el `GET`. | Dos lugares para la misma versión (header en el detalle, cuerpo en la lista). Un `ErrorType` nuevo o un mapeo especial en `ApiResults`. El plan pasa a 412. Más código de headers en el endpoint y en Angular. |

Recomendación: **A**. Es lo que encaja con la lista del paso 7 y con `ApiResults`, y es lo que hace Google. B es lo que hace Azure; tiene sentido cuando hay clientes externos genéricos, y hoy el único cliente es el frontend del repo.

## 3. Qué pasa si el cliente no manda la versión, y el `PUT` repetido

**Versión ausente**

- RFC 6585 define 428: "the origin server requires the request to be conditional. Its typical use is to avoid the "lost update" problem" ([RFC 6585 §3](https://www.rfc-editor.org/rfc/rfc6585.html#section-3)). Es para la forma con headers (opción B).
- AIP-154: "If the user does not send an etag value at all, the service should permit the request". Un servicio "may require users to send etags all the time" y rechazarlo con `INVALID_ARGUMENT` (400) ([AIP-154](https://google.aip.dev/154)).

| Opción | Qué ve el cliente | Qué ganás | Qué perdés |
|---|---|---|---|
| Exigirla: 400 con `errors.Version` (o 428 con la forma B) | Sin versión, el `PUT` no se aplica | Ningún cliente puede caer en el lost update. Encaja con el validator de la slice: `UpdateTaskRequestValidator` ya existe. | Rompe a cualquier cliente que no la mande; hoy son el frontend, el smoke y los tests, que cambian en el mismo PR. |
| Aceptarla ausente: gana la última escritura | Sin versión, el `PUT` se aplica como hoy | Compatible con v1 tal cual (AIP-180 permite agregar campos). | El defecto sigue para quien no manda versión. Dos comportamientos que probar. |

Recomendación: **exigirla**, con `uint? Version` en el record para distinguir "no vino" de 0, y una regla `NotNull` en el validator. Como en el paso 7, se cambia v1 en el lugar porque el único cliente cambia en el mismo PR. [INFERENCIA: System.Text.Json deja `null` en un `uint?` ausente; no lo probé]

**El `PUT` repetido**

- Hoy, `Update_PersistsFinalState_Idempotently` manda dos veces el mismo `PUT` y espera 200 las dos (`UpdateTaskTests.cs:24-35`), y el smoke lo repite (`smoke-api.mjs:208-212`). Ese test es la evidencia de un paso anterior: el `PUT` lleva el estado final, así que repetirlo es seguro.
- Con versión, el segundo pedido lleva la versión vieja: el primero ya la cambió. Entonces da 409, aunque el estado pedido ya es el actual.
- RFC 9110 lo prevé: si la condición falla pero "the request is a state-changing operation that appears to have already been applied", el servidor "MAY respond with a 2xx". Advierte el riesgo con clientes que "are making change requests that are very similar but not cooperative" ([RFC 9110 §13.1.1](https://www.rfc-editor.org/rfc/rfc9110.html#name-if-match)).
- Azure lo nombra como costo de usar un número de versión: "interferes with client's ability to retry update requests" ([Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| Estricta: versión vieja = 409 siempre. El test repite el `PUT` con la versión nueva que devolvió el primero. | Una sola regla, fácil de explicar. | Un reintento real (el primer 200 se perdió en la red) da 409, y el usuario ve "la tarea cambió" cuando el cambio era suyo. El test deja de probar el reintento. |
| Tolerante: versión vieja **y** estado guardado distinto = 409; versión vieja y mismo estado = 200 con la tarea actual, sin escribir. | El reintento sigue siendo seguro, como hoy. Es lo que RFC 9110 permite. | Una rama más en la slice, y una comparación en memoria antes de guardar. |

Recomendación: **tolerante**, porque el repo ya prometió que repetir el `PUT` es seguro y el paso 9 trata justo de reintentos. Es decisión de Tobi.

## 4. Cómo detecta EF Core el conflicto y cómo llega al catálogo

**El valor original manda**

- EF usa el valor **original** en el `WHERE`. El tutorial de ASP.NET Core lo explica para una página que lee en un pedido y guarda en otro: "OriginalValue is what EF Core uses in the WHERE clause", y antes de asignarlo "OriginalValue has the value that was in the database when FirstOrDefaultAsync was called in this method. This value might be different from what was displayed on the Edit page". La solución es `_context.Entry(departmentToUpdate).Property(d => d.ConcurrencyToken).OriginalValue = Department.ConcurrencyToken;` ([ASP.NET Core, Concurrency](https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/concurrency)).
- **En este repo** pasa lo mismo: `UpdateTask` carga la fila con `SingleOrDefaultAsync` en el mismo pedido (`UpdateTask.cs:32`). Sin `OriginalValue`, el `WHERE` usaría la versión recién leída y solo atraparía la carrera de milisegundos entre la carga y el guardado, no la pestaña que lleva cinco minutos abierta.
- Experimento EF: una carga nueva con `OriginalValue = <versión vieja>` generó `UPDATE ... WHERE "Id" = @p1 AND xmin = @p2 RETURNING xmin` y tiró `DbUpdateConcurrencyException` con "expected to affect 1 row(s), but actually affected 0 row(s)".

**Hallazgo del experimento: si nada cambió, no hay `UPDATE` y no hay control**

- EF solo manda las columnas modificadas. En el experimento, con `OriginalValue` viejo y sin cambiar ninguna propiedad, `SaveChanges` no mandó ningún `UPDATE` y "guardó" sin conflicto.
- Hoy `UpdateTask` siempre cambia `UpdatedAt = _clock.GetUtcNow()` (`UpdateTask.cs:41`), así que siempre hay `UPDATE`. Pero un test con `FakeTimeProvider` fijo (ya se usa en `ListTasksTests.cs`) y el mismo título y estado no cambiaría nada, y una versión vieja pasaría. [INFERENCIA, a partir del experimento]
- Con la opción tolerante de la sección 3, la slice compara la versión en memoria antes de guardar, y este caso queda cubierto. Con la opción estricta, conviene la misma comparación: `if (item.Version != request.Version)` antes de escribir.

**Carga + comparación + token: dónde atrapa cada cosa**

1. Comparación en memoria (`item.Version != request.Version`): atrapa la pestaña vieja, que es el caso de la UI.
2. `OriginalValue` + `WHERE xmin = @p`: atrapa la carrera, cuando otro pedido escribe entre la carga y el guardado. PostgreSQL en Read Committed hace esperar al segundo `UPDATE` y después "The search condition of the command (the WHERE clause) is re-evaluated to see if the updated version of the row still matches" ([PostgreSQL, Transaction Isolation](https://www.postgresql.org/docs/current/transaction-iso.html)). El `xmin` cambió, entonces no coincide y afecta 0 filas.

**Del error al catálogo, sin `try/catch` en el endpoint**

- El endpoint no accede a EF (`docs/conventions.md:71`), y `ApiResults` es el único mapeo a HTTP (`docs/conventions.md:74`). Entonces el `catch` va en la slice, alrededor de `SaveChangesAsync`, y devuelve un `Error` del catálogo. `UpdateTaskEndpoint` no cambia.
- No conviene un `IExceptionHandler` que traduzca la excepción: `GlobalExceptionHandler` vive en `Infrastructure/` (`backend/Infrastructure/Middlewares/GlobalExceptionHandler.cs:13`) y no puede nombrar `TaskErrors` (`docs/conventions.md:22`). Hoy convierte cualquier excepción en 500 `Http.Unexpected`.
- Error nuevo en `backend/Features/Tasks/TaskErrors.cs`: `Error.Conflict(code: "Task.Conflict", description: "...")`. DUNE0014 lo acepta: está en un `*Errors.cs` y empieza con `Task.` (`docs/conventions.md:76`).
- Borrado durante la edición: si otro pedido borró la tarea entre la carga y el guardado, la excepción es la misma. `entry.GetDatabaseValuesAsync()` devuelve "null if the entity does not exist in the database" ([Learn, GetDatabaseValuesAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.changetracking.entityentry.getdatabasevaluesasync)). Con eso la slice puede devolver `TaskErrors.NotFound(id)` en vez de `Conflict`. Es un detalle; si no entra, ese caso raro da 409.
- Forma (no código final):

  ```csharp
  if (item.Version != request.Version)
      return SameState(item, request) ? TaskResponses.From(item) : TaskErrors.Conflict;
  _db.Entry(item).Property(t => t.Version).OriginalValue = request.Version.Value;
  try { await _db.SaveChangesAsync(ct); }
  catch (DbUpdateConcurrencyException) { return TaskErrors.Conflict; }
  ```

  La primera línea `return SameState(...)` solo existe con la opción tolerante.

- Después de guardar, EF lee el `xmin` nuevo con `RETURNING xmin` (experimento EF) y lo deja en `item.Version`. `TaskResponses.From(item)` lo devuelve sin otra consulta.

## 5. El test de "dos actualizaciones concurrentes"

**Respuesta:** dos tests. Uno secuencial con versión vieja, que es determinista y es el caso de la UI. Otro con los dos pedidos en paralelo, que también es determinista por cómo está hecha la slice.

- Estilo del repo: `IClassFixture<ApiFixture>`, `TestSession` sobre HTTP real y PostgreSQL real (`UpdateTaskTests.cs:7-17`, `docs/conventions.md:101`). `TestSession.CopyTo(HttpClient)` copia las cookies a otro cliente (`backend/Tests/Support/TestSession.cs:110-120`): sirve para "otra pestaña" del mismo usuario.
- **Test 1, versión vieja (el concepto):** Alice crea "Comprar pan" y lee la versión `v0`. Pestaña A: `PUT { title: "Comprar pan integral", isCompleted: false, version: v0 }` → 200 con `v1`. Pestaña B: `PUT { title: "Comprar pan", isCompleted: true, version: v0 }` → 409, `code: "Task.Conflict"` (con `ProblemAssert`, `backend/Tests/Support/ProblemAssert.cs`). `GET` → título "Comprar pan integral", `isCompleted: false`.
- **Test 2, en paralelo:** los dos `PUT` con `v0`, lanzados con `Task.WhenAll` desde dos clientes. Afirma: exactamente un 200 y un 409, y que el estado final es el del 200. El orden de llegada cambia quién gana, pero no el resultado: si B carga después de que A guardó, la comparación en memoria lo frena; si carga antes, el `WHERE xmin` lo frena (sección 4). [INFERENCIA, a partir del diseño y de la documentación de PostgreSQL; no lo corrí]
- **¿Es lo mismo el secuencial que la carrera?** No del todo.
  - El secuencial prueba el lost update de la UI: la lectura vieja de una pestaña. Con la comparación en memoria, no llega a la base.
  - Solo el paralelo puede pasar por el `WHERE xmin`, y no hay garantía de que lo haga: depende de que los dos carguen antes de que uno guarde.
  - Forzar ese camino pide un gancho dentro de la slice (un interceptor de EF que escriba la fila justo antes del `UPDATE`). Eso es un test de infraestructura, no de una slice por HTTP. No lo recomiendo para este paso.
- Recomendación: **los dos tests**. El secuencial es el rojo y el que enseña; el paralelo es el que pide el plan ("dos actualizaciones concurrentes"). El camino del `WHERE xmin` queda probado en el experimento EF de este informe, y el cierre lo dice.
- **Migración:** un test que afirme el esquema no aplica con `xmin`. La evidencia del plan, "migración aplicada por CLI sin editar a mano", queda en: el archivo generado por `dotnet ef migrations add`, `dotnet ef migrations list` mostrándola aplicada, y el diff sin ediciones.
- **Validación:** `PUT` sin `version` → 400 con `errors.Version` (si Tobi elige exigirla).
- **Test vigente:** `Update_PersistsFinalState_Idempotently` cambia según la decisión de la sección 3: con la opción tolerante, el segundo `PUT` con `v0` sigue dando 200; con la estricta, manda `v1`.

## 6. Frontend

**Hoy**

- `TaskResponse` en Angular tiene `id`, `title`, `isCompleted`, `createdAt`, `updatedAt` (`frontend/src/app/features/tasks/api/task-response.ts`). `UpdateTaskRequest` tiene `title` e `isCompleted` (`api/update-task-request.ts`).
- `Tasks.setCompleted` manda el título que tiene la fila (`tasks.ts:104-108`); `saveTitle` manda el estado que tiene la fila (`tasks.ts:119-126`). Los dos mandan el estado completo de una lectura que puede ser vieja: es el lost update.
- `runRowAction` reemplaza la fila con la respuesta del `PUT` (`tasks.ts:190-201`). Con versión, eso deja la versión nueva en la fila sin hacer nada más.
- El error de una acción va en `actionError`, con `describeHttpError` (`tasks.ts:197`). Para 409 devuelve `detail ?? 'Ya existe un registro con esos datos.'` (`frontend/src/app/core/http-error.ts:46-47`). El backend pone la descripción en `title`, no en `detail` (`docs/decisions.tsv:124`), así que el usuario vería "Ya existe un registro con esos datos.". Es falso para este caso.
- `ProblemDetails` del frontend no tiene `code` (`frontend/src/app/core/problem-details.ts`), aunque la regla dice "El cliente decide por `code`" (`docs/conventions.md:75`).

**Propuesta**

- `TaskResponse` (Angular) suma `version: number`; `UpdateTaskRequest` suma `version: number`. `setCompleted` y `saveTitle` mandan `task.version`. La versión viaja con cada fila; no hace falta guardarla aparte. [INFERENCIA: un `uint` cabe en un `number` de JavaScript sin perder precisión]
- Ante un 409 con `code: "Task.Conflict"`:
  - `Tasks` muestra un mensaje propio en `actionError`, por ejemplo "Otra pestaña o dispositivo cambió esta tarea. Se muestran los datos actuales.".
  - Después recarga la página actual con `load()`, que ya existe (`tasks.ts:60-72`).
  - El mensaje vive en la feature de tareas: `core/` no nombra features (`docs/conventions.md:39`). `ProblemDetails` suma `code?: string` para que `Tasks` decida por el código.
- Qué ve el usuario con la edición abierta: si guarda un título y recibe 409, la recarga cambia `task()` y el `linkedSignal` del borrador vuelve al título actual (`frontend/src/app/features/tasks/task-row/task-row.ts`). El texto que escribió se pierde. Alternativa: dejar la edición abierta con su texto y mostrar el mensaje. Es una decisión de interfaz; recomiendo recargar, porque es lo que sugiere EF Core para el caso simple: "inform the user that the update failed due to conflicting changes; the user can then load the new data and try again" ([EF Core, Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency#resolving-concurrency-conflicts)).
- Recargar la fila sola con `GET /tasks/{id}` también es posible. Pide un método nuevo en `TasksApi`. `load()` ya existe y deja la página coherente. [INFERENCIA]
- Con la forma B (ETag), `TasksApi.update` necesitaría `observe: 'response'` para leer el header y `headers: { 'If-Match': ... }` para mandarlo ([angular.dev, Making requests](https://angular.dev/guide/http/making-requests)). El proxy de desarrollo sirve la API en el mismo origen (`frontend/proxy.conf.json`), así que no hace falta exponer el header por CORS. [INFERENCIA]

**Vitest**

- Si el texto del 409 de tareas sale a una función pura de la feature (por ejemplo `describeTaskError(err)`), se prueba en Vitest. Hoy `Tasks` no tiene spec, porque es un componente de ruta (`docs/conventions.md:106`). Es opcional.

**E2E (`frontend/e2e/tasks.spec.ts`)**

- `renaming a row sends PUT 200 with the new title` afirma `expect(bodies.at(-1)).toEqual({ title: 'Renombrada en linea', isCompleted: false })` (`tasks.spec.ts:145`). Con `version` en el cuerpo, `toEqual` falla. Pasa a `toMatchObject` o suma `version: expect.any(Number)`.
- `the checkbox sends PUT with isCompleted true...` afirma lo mismo (`tasks.spec.ts:186`).
- E2E nuevo del concepto: el mismo usuario con una segunda página en el mismo contexto (comparte la cookie, como en `tabs.spec.ts`). Página 1 renombra; página 2, sin recargar, marca el checkbox. Afirma: `PUT` 409, el mensaje en el `role="alert"`, y que la fila muestra el título nuevo sin marcar.
- El e2e corre una sola vez, al cerrar el paso, porque cambia el frontend y el contrato (`AGENTS.md`, "Verificación").

## 7. Paso 7, la lista y OpenAPI

- **La lista necesita la versión en cada fila.** La UI edita desde la lista (`tasks.ts:104-126`), sin pedir el detalle. `ListTasks` proyecta con `TaskResponses.Projection` (`backend/Features/Tasks/TaskResponses.cs:8-9`), igual que `GetTask`, `CreateTask` y `UpdateTask`. Sumar `t.Version` a la proyección y a `TaskResponse` llega a las cuatro respuestas de una vez.
- La consulta de la lista suma `t.xmin` al `SELECT`. No cambia el `WHERE`, el orden ni el cursor del paso 7. [INFERENCIA: no corrí `ToQueryString()` con la proyección real]
- `TaskResponse` vive en la raíz de la feature porque la usan varias slices (`docs/conventions.md:18`); no es un contrato entre features. No toca `Contracts/`.
- **Versionado:** sumar `version` a la respuesta es compatible; exigirlo en el `PUT` no. Es la misma situación del paso 7, donde Tobi eligió cambiar v1 en el lugar (`progreso.md:7`).
- **OpenAPI:** los endpoints devuelven `Task<IResult>` sin `Produces` (`UpdateTaskEndpoint.cs:9-20`; búsqueda de `Produces` en `backend/`: cero resultados), y no hay tests del documento OpenAPI. El documento no describe hoy la respuesta, así que `version` no cambia nada ahí. Con la forma B, `[FromHeader(Name = "If-Match")]` sí aparecería como parámetro. [INFERENCIA, no generé el documento]
- **Smoke (`.agents/skills/verify-template/scripts/smoke-api.mjs`):** el `PUT` ajeno de Bob (`smoke-api.mjs:201-204`) y los `PUT` de Alice (`208-212`) mandan `version`. El 403 de Bob sigue antes que la versión, porque la slice comprueba el permiso primero (`UpdateTask.cs:36-37`, `docs/conventions.md:72`).

## 8. `DeleteTask`: ¿entra?

**Respuesta:** recomiendo que no entre con versión del cliente, y anotar un pendiente.

- Con `xmin` configurado como token, EF suma la condición también al borrado. Experimento EF: `DELETE FROM "TaskItems" WHERE "Id" = @p0 AND xmin = @p1`. Como `DeleteTask` carga la fila en el mismo pedido (`backend/Features/Tasks/DeleteTask/DeleteTask.cs:22-30`), la versión es la recién leída: solo atrapa la carrera de milisegundos.
- **Hallazgo de hoy, sin token:** dos `DELETE` de la misma tarea que cargan antes de que el primero borre. El experimento EF, sin token, dio `DbUpdateConcurrencyException` en el segundo, porque EF espera 1 fila afectada en todo `UPDATE` y `DELETE` ("A concurrency violation occurs when an unexpected number of rows are affected during save", [Learn, DbUpdateConcurrencyException](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbupdateconcurrencyexception)). En el repo, esa excepción llega a `GlobalExceptionHandler` y sale como 500 `Http.Unexpected`. [INFERENCIA por el código; no lo reproduje por HTTP] Lo mismo vale hoy para un `PUT` que pierde contra un `DELETE`.
- Mandar la versión en el `DELETE` es posible: Azure lo describe con `If-Match` y 412 ([Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)), y EF lanza la misma excepción. Pero borrar una tarea que otro cambió hace un minuto no pierde un cambio del usuario: lo borra a propósito.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| Fuera del paso, con un pendiente en `progreso.md` ("el `DELETE` concurrente da 500") | Un concepto por paso (regla 1 y regla 7). | El 500 del borrado doble sigue hasta otro paso. |
| Solo atrapar la excepción en `DeleteTask` (sin versión del cliente) y responder 404 o 204 | Arregla el 500 con dos líneas. | Es otro cambio observable dentro del paso. |
| Versión del cliente también en `DELETE` | Contrato simétrico con el `PUT`. | Cambia otro contrato, el diálogo de borrado y sus e2e. |

Recomendación: **fuera del paso, con un pendiente**. La misma excepción en `UpdateTask` sí entra, porque es el concepto.

## 9. El rojo real

**Respuesta:** el test 1 de la sección 5, escrito antes del cambio, da rojo en una aserción de estado.

- Hoy `TaskResponse` no tiene `version` y `UpdateTaskRequest` no la lee. System.Text.Json ignora por defecto un campo de más en el JSON. [INFERENCIA: es el comportamiento por defecto de `JsonUnmappedMemberHandling.Skip`; no lo probé en este repo]
- Si el test lee la versión con `TryGetProperty` (o la omite), corre sobre el código de hoy y falla así:
  - El segundo `PUT` (pestaña B, título viejo + checkbox) da **200**: `Assert.Equal(HttpStatusCode.Conflict, ...)` falla con `Expected: Conflict, Actual: OK`.
  - Si se afirma el estado antes del status, el `GET` muestra **"Comprar pan"** y no "Comprar pan integral": el renombre se perdió.
- Si el test lee `version` con `GetProperty`, falla con `KeyNotFoundException`. Es un rojo, pero por el motivo equivocado: no muestra el lost update. Conviene evitarlo.
- El experimento EF reprodujo el mismo defecto a nivel de base: dos contextos leen la fila; A cambia el título; B marca `IsCompleted` y guarda sin error (`UPDATE "TaskItems" SET "IsCompleted" = @p0 WHERE "Id" = @p1`). Con el token, el `UPDATE` de B salió con `AND xmin = @p2` y falló.
- Por HTTP el defecto es más fuerte que en el experimento: `UpdateTask` asigna **las dos** propiedades con lo que manda el cliente (`UpdateTask.cs:39-40`). El título viejo de B difiere del guardado, así que EF lo escribe y pisa el de A. [INFERENCIA por el código]
- El test 2 (paralelo) también da rojo hoy: los dos pedidos dan 200.

## 10. Choques con los docs y con el plan

1. **"Cambia" del plan no nombra el frontend.** `plan.md:301` nombra `TaskItem.cs`, `TaskItemConfiguration.cs`, la slice `UpdateTask`, la migración y tests. Si el `PUT` exige `version`, cambia el contrato y la regla 3 de `AGENTS.md` obliga a sumar el frontend, el smoke y los e2e. Tampoco nombra `TaskResponse.cs`, `TaskResponses.cs` ni `TaskErrors.cs`, que cambian con cualquier opción. Conviene corregir esa línea del plan.
2. **409 frente a 412.** Con la versión en el cuerpo, 409 coincide con RFC 9110 §15.5.10 y con AIP-154. Si Tobi elige ETag + `If-Match`, el estándar es 412 y el "Verifica" del plan cambia. No lo resuelvo.
3. **"Migración aplicada por CLI" con `xmin`.** La migración se genera por CLI y no se edita, pero no cambia el esquema. La verificación del plan se cumple en la letra y prueba poco. Con un `int Version` (opción B de la sección 1) la migración tiene DDL real.
4. **El test de idempotencia del `PUT`.** `Update_PersistsFinalState_Idempotently` y el smoke esperan que repetir el `PUT` dé 200. Con la versión, eso deja de ser cierto salvo con la opción tolerante (sección 3).
5. **Fase del paso.** `progreso.md:7` dice "Fase 2, paso 8"; `plan.md:297` pone el paso 8 en la Fase 3 ("Escritura correcta"). Es un error de texto. Lo anoto; no cambia nada.
6. **El mensaje de 409 en `core/`.** `describeHttpError` asume que todo 409 es un duplicado (`http-error.ts:47`). El conflicto de versión necesita su texto, y la regla de `core/` obliga a ponerlo en la feature.

## Decisiones abiertas para Tobi

1. **Token de concurrencia.**
   - A. `xmin` (`uint Version` + `IsRowVersion()`): ganás cero columnas y cero código para subir la versión; perdés portabilidad, y la migración no cambia el esquema.
   - B. `int Version` propio: ganás un valor legible y una migración con DDL real; perdés que cada escritura tiene que subirlo a mano.
   - C. `Guid Version` propio: como B, con un valor más largo y sin orden.
   - Recomiendo **A**, porque el repo ya está atado a PostgreSQL y es lo que documenta Npgsql.
2. **Cómo viaja la versión.**
   - A. Campo `version` en el cuerpo, 409 `Task.Conflict`: ganás un solo lugar, también para la lista, sin tocar `ApiResults`; perdés el mecanismo HTTP estándar.
   - B. ETag + `If-Match`, 412: ganás el estándar de RFC 9110 y el de Azure; perdés simplicidad (la lista igual necesita la versión en el cuerpo, un mapeo nuevo en `ApiResults`, headers en Angular) y el plan pasa a 412.
   - Recomiendo **A**: encaja con la lista del paso 7 y es lo que hace Google.
3. **Versión ausente.**
   - Exigirla (400 `errors.Version`): ganás que nadie caiga en el lost update; perdés compatibilidad con un cliente que no la mande (hoy no hay ninguno fuera del repo).
   - Aceptarla (gana la última escritura): ganás compatibilidad; perdés el arreglo para ese cliente.
   - Recomiendo **exigirla**, igual que en el paso 7 se cambió v1 en el lugar.
4. **`PUT` repetido con la versión vieja.**
   - Estricto (siempre 409): ganás una regla única; perdés el reintento seguro que hoy prueba `Update_PersistsFinalState_Idempotently`.
   - Tolerante (409 solo si el estado guardado es distinto): ganás que el reintento siga seguro, como permite RFC 9110; perdés una rama más en la slice.
   - Recomiendo **tolerante**.
5. **Qué ve el usuario ante un 409.**
   - Mensaje + recargar la página: ganás datos frescos con código que ya existe; perdés el texto que el usuario estaba escribiendo.
   - Mensaje + edición abierta con su texto: ganás no perder lo escrito; perdés simplicidad (la fila muestra datos viejos hasta que el usuario recarga).
   - Recomiendo **recargar**.
6. **`DeleteTask`.**
   - Fuera del paso, con un pendiente: ganás un concepto por paso; el 500 del borrado doble sigue.
   - Atrapar la excepción en `DeleteTask`: arreglás el 500; sumás otro cambio observable.
   - Recomiendo **fuera, con pendiente**.

## Qué quedó sin comprobar

- No corrí nada del repo: ni tests, ni smoke, ni e2e, ni `dotnet ef` sobre el proyecto real. El experimento EF usó un modelo mínimo con las mismas columnas que importan.
- El test paralelo de la sección 5: el razonamiento de que siempre da un 200 y un 409 sale del diseño y de la documentación de PostgreSQL; no lo corrí.
- Qué hace `VACUUM FULL` con `xmin`, y el efecto de la replicación lógica. No encontré fuente primaria.
- Que System.Text.Json deje `null` en un `uint?` ausente y que ignore un campo de más: es el comportamiento por defecto documentado en general, no lo probé con la configuración del repo.
- El directorio por defecto de `dotnet ef migrations add` cuando el snapshot está en `Infrastructure/Persistence/Migrations/`. Recomiendo `--output-dir` explícito para no depender de eso.
- Que el `DELETE` doble dé 500 por HTTP en el repo: lo infiero del experimento EF y de `GlobalExceptionHandler`.
- El documento OpenAPI generado con los cambios.
- Que la respuesta 201 de `CreateTask` (`CreateTask.cs:46`, `TaskResponses.From(item)`) ya traiga el `xmin` de la fila nueva. Con `ValueGenerated.OnAddOrUpdate`, EF debería leerlo con `RETURNING` también en el `INSERT`; el experimento no imprimió la versión después de insertar. [INFERENCIA]

## Fuentes

- https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- https://learn.microsoft.com/en-us/aspnet/core/data/ef-rp/concurrency
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbupdateconcurrencyexception
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.changetracking.entityentry.getdatabasevaluesasync
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.headers.requestheaders.ifmatch
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.iheaderdictionary.etag
- https://www.npgsql.org/efcore/modeling/concurrency.html
- https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Metadata/Conventions/NpgsqlPostgresModelFinalizingConvention.cs
- https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Migrations/NpgsqlMigrationsSqlGenerator.cs
- https://github.com/npgsql/efcore.pg/blob/v10.0.3/test/EFCore.PG.FunctionalTests/Migrations/MigrationsNpgsqlTest.cs
- https://github.com/npgsql/efcore.pg/blob/v10.0.3/test/EFCore.PG.Tests/Metadata/Conventions/NpgsqlPostgresModelFinalizingConventionTest.cs
- https://www.postgresql.org/docs/current/ddl-system-columns.html
- https://www.postgresql.org/docs/current/routine-vacuuming.html
- https://www.postgresql.org/docs/current/transaction-iso.html
- https://www.rfc-editor.org/rfc/rfc9110.html
- https://www.rfc-editor.org/rfc/rfc6585.html
- https://google.aip.dev/154
- https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md
- https://angular.dev/guide/http/making-requests
