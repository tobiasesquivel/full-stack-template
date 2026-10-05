# Health

## Purpose

Probes operacionales anónimos bajo /api/v1/health.

## Requirements

### Requirement: Liveness independiente

GET /live MUST comprobar solo que el proceso atiende HTTP. MUST NOT depender de PostgreSQL, Identity, collector ni migraciones.

#### Scenario: Base caída
- GIVEN un proceso ya iniciado y PostgreSQL no disponible.
- WHEN se consulta /api/v1/health/live.
- THEN MUST devolver 200.

### Requirement: Readiness real

GET /ready MUST comprobar PostgreSQL y ausencia de migraciones pendientes. MUST devolver 503 si falla cualquier comprobación y 200 cuando puede servir operaciones persistentes. Respuestas MUST NOT exponer connection strings, credenciales ni excepciones internas.

#### Scenario: Dependencia caída
- GIVEN PostgreSQL no disponible.
- WHEN se consulta /api/v1/health/ready.
- THEN MUST devolver 503 con información no sensible.

#### Scenario: Esquema pendiente
- GIVEN PostgreSQL accesible y una migración pendiente.
- WHEN se consulta readiness.
- THEN MUST devolver 503.

#### Scenario: Esquema listo
- GIVEN PostgreSQL accesible y todas las migraciones aplicadas.
- WHEN se consulta readiness.
- THEN MUST devolver 200.

### Requirement: Arranque observable

El arranque MUST aplicar migraciones y seed idempotente con errores Identity comprobados. Un fallo fatal MUST producir salida no cero y cerrar Serilog. Compose MUST usar readiness para ordenar dependencias sin reemplazar las pruebas de fallo posteriores al arranque.
