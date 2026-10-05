# Observability

## Purpose

Contrato de logs, métricas y tracing. Registrar exporters no demuestra exportación.

## Requirements

### Requirement: Tres señales OTLP

Serilog MUST emitir logs estructurados por OTLP. OpenTelemetry MUST exportar métricas y spans. Requests MUST incluir identificadores trace y span correlacionados. ASP.NET, HttpClient, runtime y PostgreSQL MUST aportar las señales disponibles. Collector MUST recibir las tres señales y tener configuración reemplazable para un destino externo.

#### Scenario: Flujo real
- GIVEN API, PostgreSQL y collector en ejecución.
- WHEN se realiza un registro y una operación Tasks.
- THEN el collector MUST recibir logs, métricas y spans del flujo.
- AND el log de request MUST correlacionarse con el trace exportado.

### Requirement: Datos acotados y no sensibles

Logs MUST NOT contener bodies, Authorization, Cookie, contraseñas, refresh tokens ni access tokens. Métricas de negocio MUST usar etiquetas acotadas sin títulos ni identificadores de usuarios o tareas. Errores inesperados MUST devolver ProblemDetails 500 sin detalles internos.

#### Scenario: Petición con credenciales
- GIVEN un login que lleva contraseña y cookies.
- WHEN se capturan logs y telemetry exportada.
- THEN MUST NOT aparecer los valores sensibles.

### Requirement: Despliegue configurable

Compose MUST ejecutar PostgreSQL 18 con volumen persistente, collector, API sin root y frontend del mismo origen. JWT.Key y ConnectionStrings.DefaultConnection MUST venir de secrets o entorno sin valores por defecto secretos. Un smoke MUST observar señales recibidas y comportamiento HTTP, nunca inferir éxito de un registro DI.
