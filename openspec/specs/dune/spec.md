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

### Requirement: APIs prohibidas

BannedApiAnalyzers MUST prohibir reloj local, Task.Wait, Task.Result y SQL raw sin parámetros. Alias y nombres calificados MUST NOT eludir diagnósticos. TimeProvider y SQL interpolado parametrizado MUST ser alternativas permitidas. Migraciones históricas y snapshot MUST tener excepciones exactas justificadas sin alterar historia para satisfacer las reglas.

### Requirement: Estructura y pruebas colocadas

El gate Node MUST exigir Features/<Name>/Tests y su exclusión de producción. MUST rechazar dependencias legacy, fuentes OpenSpec duplicadas, repositorios Git anidados y artefactos generados o binarios versionados. Cada regla MUST tener prueba válida y violación deliberada que observe diagnóstico o exit code. MUST NOT usar búsquedas de cadenas incidentales como prueba del comportamiento del analyzer.

#### Scenario: Repositorio inválido
- GIVEN un fixture con tests incluidos en producción o una segunda raíz OpenSpec.
- WHEN corre el gate de estructura.
- THEN MUST devolver código no cero.

#### Scenario: Repositorio válido
- GIVEN un fixture que respeta colocación y exclusiones.
- WHEN corre el mismo gate.
- THEN MUST devolver cero.

### Requirement: Gates de PR atómico

CI MUST ejecutar estructura, analyzer tests, build backend, tests HTTP con PostgreSQL real, tests y build frontend, y smoke Docker. MUST NOT usar continue-on-error ni omitir analyzers o tests. Cambio observable de contrato MUST incluir backend, frontend, tests y spec en el mismo PR, sin presupuesto artificial de líneas. Resultados MUST reportar checks realmente ejecutados. Una entrega sin compilación o runtime observado MUST figurar NOT VERIFIED. Disponibilidad del SDK MUST NOT confundirse con gates aprobados.
