# Dune

## Purpose

Restricciones ejecutables de arquitectura y entrega del monorepo. Los requisitos no afirman que CI haya corrido.

## Requirements

### Requirement: Análisis semántico

Roslyn MUST comprobar por símbolos que Domain usa solo BCL, Shared.Domain y el Domain propio. Endpoints MUST NOT acceder a EF, Persistence ni UserManager. Infrastructure MUST NOT invocar módulos o endpoints de aplicación, excepto el registro explícito de composición AddBackendInfrastructure. Una feature MUST NOT llamar implementation de otra, aunque MAY referenciar Domain y Contracts. Namespace MUST coincidir con la ruta. DbSet MUST NOT ocultar otro DbSet. Cargas individuales MUST NOT combinar Id y OwnerId.

#### Scenario: Referencia prohibida
- GIVEN una compilación de prueba con un símbolo EF en Domain o acceso a persistencia desde un endpoint.
- WHEN ejecuta el analyzer.
- THEN MUST observar el diagnóstico esperado con severidad error.

#### Scenario: Referencias válidas
- GIVEN Domain puro, módulo directo y adapter HTTP thin.
- WHEN ejecuta el analyzer.
- THEN MUST NOT emitir los diagnósticos de dependencias prohibidas.

#### Scenario: Adapter y módulo en un archivo mixto
- GIVEN un archivo de caso de uso con declaraciones parciales de módulo y endpoint, sin Endpoint en el nombre de archivo.
- WHEN el analyzer procesa los símbolos de cada declaración.
- THEN MUST permitir EF en el módulo y MUST rechazar EF, Persistence o UserManager dentro del tipo endpoint, incluidos tipos anidados.

### Requirement: APIs prohibidas

BannedApiAnalyzers MUST prohibir reloj local, Task.Wait, Task.Result y SQL raw sin parámetros. Alias y nombres calificados MUST NOT eludir diagnósticos. TimeProvider y SQL interpolado parametrizado MUST ser alternativas permitidas. Migraciones históricas y snapshot MUST tener excepciones exactas justificadas sin alterar historia para satisfacer las reglas.

### Requirement: Estructura y pruebas colocadas

El gate Node `dune/check-structure.mjs` MUST descubrir cada directorio bajo `backend/Features/` y exigir `Tests/` con fuentes `*.cs` en cada uno, con exclusión de producción. El inventario de tests MUST usar el glob `../Features/*/Tests/**/*.cs` sin enumerar features. MUST rechazar dependencias legacy, fuentes OpenSpec duplicadas, repositorios Git anidados y artefactos generados o binarios versionados. Cada regla MUST tener prueba válida y violación deliberada que observe diagnóstico o exit code. MUST NOT usar búsquedas de cadenas incidentales como prueba del comportamiento del analyzer.

#### Scenario: Repositorio inválido
- GIVEN un fixture con tests incluidos en producción o una segunda raíz OpenSpec.
- WHEN corre el gate de estructura.
- THEN MUST devolver código no cero.

#### Scenario: Repositorio válido
- GIVEN un fixture que respeta colocación y exclusiones.
- WHEN corre el mismo gate.
- THEN MUST devolver cero.

#### Scenario: Feature nueva sin Tests
- GIVEN un fixture con un directorio nuevo bajo `backend/Features/` sin `Tests/` con fuentes.
- WHEN corre el gate de estructura.
- THEN MUST devolver código no cero.

#### Scenario: Feature nueva con Tests
- GIVEN un fixture con un directorio nuevo bajo `backend/Features/` con `Tests/` y fuentes incluidas por el glob.
- WHEN corre el gate de estructura.
- THEN MUST devolver cero.

#### Scenario: Caché local y artefacto indexado
- GIVEN una caché Angular ignorada y no indexada.
- WHEN corre el gate de estructura.
- THEN MUST devolver cero si las demás reglas se cumplen.
- GIVEN un artefacto ignorado agregado al índice Git, aunque ya no exista en el directorio de trabajo.
- WHEN corre el mismo gate.
- THEN MUST devolver código no cero sin sustituir el índice por un recorrido del directorio.

### Requirement: Gates de PR atómico

CI MUST ejecutar estructura (`node dune/check-structure.mjs`, `node --test dune/check-structure.test.mjs`), reglas C# (`dotnet build TemplateApp.slnx`, `dotnet test dune/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj`, `dotnet run --project dune/Dune.CommentVerifier -- --root .`), frontend (`npm ci`, `npm run build`, `node --test ../dune/check-frontend.test.mjs`, `node ../dune/check-frontend.mjs --root ..`), comentarios amplios (`npm ci --prefix frontend`, `node --test dune/check-comments.test.mjs`, `node dune/check-comments.mjs --root .`), tests HTTP con PostgreSQL real y smoke Docker. MUST NOT usar continue-on-error ni omitir analyzers o tests. La política MSBuild central MUST vivir en `dune/Dune.props` con `Directory.Build.props` solo como Import, y los símbolos prohibidos MUST vivir en `dune/BannedSymbols.txt`. Cambio observable de contrato MUST incluir backend, frontend, tests y spec en el mismo PR, sin presupuesto artificial de líneas. Resultados MUST reportar checks realmente ejecutados. Una entrega sin compilación o runtime observado MUST figurar NOT VERIFIED. Disponibilidad del SDK MUST NOT confundirse con gates aprobados.

