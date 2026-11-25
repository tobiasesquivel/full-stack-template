# TemplateApp

[![Template CI](https://github.com/tobiasesquivel/full-stack-template/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/tobiasesquivel/full-stack-template/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![Angular 22](https://img.shields.io/badge/Angular-22-DD0031)](https://angular.dev/)
[![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1)](https://www.postgresql.org/)

Template full-stack con ASP.NET Core 10, Angular 22 y PostgreSQL. Trae registro, sesión con cookie, perfil y tareas personales, organizados como vertical slices dentro de features que funcionan como módulos profundos.

> [!NOTE]
> Es un laboratorio de aprendizaje. El dominio es simple a propósito: el foco es llevar el backend, paso a paso, hasta una arquitectura .NET empresarial completa. La hoja de ruta está en [plan.md](docs/evolucion/plan.md) y el estado de cada paso en [progreso.md](docs/evolucion/progreso.md).

## Qué trae

- **Vertical slices + módulos profundos**: cada feature (`Auth`, `Users`, `Tasks`, `Projects`, `Health`) agrupa sus slices y expone una interfaz chica ([ADR-0004](docs/adr/0004-vertical-slices-deep-modules.md)).
- **Dos procesos detrás de un gateway**: el monolito y el servicio de Tasks, con YARP adelante ([ADR-0005](docs/adr/0005-gateway-y-procesos-propios.md)).
- **Mediator y mensajería**: Wolverine como mediator y como bus sobre RabbitMQ entre procesos, con outbox e inbox transaccionales.
- **Sesión segura**: cookie `__Host-session` cifrada con Data Protection, antiforgery en toda escritura y rate limiting por usuario e IP.
- **Observabilidad**: logs, métricas y trazas con OpenTelemetry, Prometheus con SLOs, alertas y runbooks.
- **Frontend moderno**: Angular 22 con signals, Signal Forms y Tailwind.
- **Arquitectura ejecutable**: Dune controla las reglas con analyzers Roslyn y gates Node.
- **Pruebas reales**: integración contra PostgreSQL y RabbitMQ con Testcontainers, Vitest y Playwright.

## Empezar

### Requisitos

- [.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0) (fijado en `global.json`)
- [Node.js 26.7](https://nodejs.org/)
- [Docker](https://www.docker.com/) con Compose v2 y contenedores Linux

### Crear tu repositorio

Usá **Use this template** en GitHub, o copiá el monorepo completo sin `.git`, `node_modules`, `bin`, `obj`, `dist`, `.angular` ni `.env`. Los detalles, incluido cómo cambiar el nombre `TemplateApp`, están en [desarrollo](docs/desarrollo.md#crear-tu-repositorio).

### Levantar la aplicación

```sh
cp .env.example .env   # reemplazá POSTGRES_PASSWORD y RABBITMQ_PASSWORD
docker compose up --build --wait
```

| Servicio | URL |
|---|---|
| Frontend | http://localhost:8081 |
| API (gateway) | http://localhost:8080/api/v1 |
| Prometheus | http://localhost:9090 |

Para correr con procesos locales, configurar secrets o hablar con la API por HTTP, mirá [desarrollo](docs/desarrollo.md).

### Verificar

Un solo comando corre todos los gates, levanta una instancia aislada y prueba la API y el navegador:

```sh
E2E_CHANNEL=msedge node .agents/skills/verify-template/scripts/check-all.mjs --run-id <id>
```

La lista completa de comandos, qué revisa cada gate y cómo probar observabilidad y SLOs está en [verificación](docs/verificacion.md).

## Stack

| Capa | Tecnologías |
|---|---|
| Backend | ASP.NET Core 10, EF Core + Npgsql, Wolverine, FluentValidation, ErrorOr, Asp.Versioning, Serilog |
| Frontend | Angular 22 (signals, Signal Forms), Tailwind CSS 4, Vitest, Playwright |
| Datos y mensajes | PostgreSQL 18, RabbitMQ 4 |
| Gateway | YARP |
| Observabilidad | OpenTelemetry Collector, Prometheus |
| Pruebas backend | xUnit v3 sobre Microsoft Testing Platform, Testcontainers, WireMock.Net |
| Arquitectura | Dune: analyzers Roslyn, BannedApiAnalyzers y gates Node |

## Estructura

```text
backend/
  Features/        Auth, Users, Tasks, Projects, Health: un slice por caso de uso
  Hosts/           Monolith y Tasks: un proceso desplegable cada uno
  Infrastructure/  Persistencia, mensajería, HTTP y observabilidad compartidos
  Shared/          Tipos comunes a todas las features
  Platform/        Librería que compila Infrastructure/ y Shared/ para los dos hosts
  Tests/           Integración contra PostgreSQL real
frontend/          Angular, una carpeta por feature
gateway/           YARP delante de los dos procesos
deploy/            Collector OTLP y Prometheus con reglas de SLO
dune/              Analyzers, verificador de comentarios y gates de arquitectura
docs/              Arquitectura, convenciones, ADRs, plan y operación
```

## Documentación

| Documento | Para qué |
|---|---|
| [Arquitectura](docs/architecture.md) | Cómo está armado el sistema hoy |
| [Convenciones](docs/conventions.md) | Reglas para escribir código |
| [ADRs](docs/adr/) y [decisiones](docs/decisions.tsv) | Decisiones tomadas y su porqué |
| [Glosario](CONTEXT.md) | Vocabulario del dominio |
| [Desarrollo](docs/desarrollo.md) | Configuración, ejecución y API HTTP |
| [Verificación](docs/verificacion.md) | Gates, pruebas y evidencia |
| [SLOs y runbooks](docs/operacion/slos.md) | Objetivos de servicio y qué hacer ante cada alerta |
| [Plan de evolución](docs/evolucion/plan.md) | Hoja de ruta por fases |

## Contribuir

Cada paso del plan va en su rama y su PR, con un solo cambio observable. Si cambia un contrato HTTP, el PR incluye backend, frontend y tests. Adjuntá los comandos de [verificación](docs/verificacion.md) con su resultado por etapa (`PASS`, `FAIL` o `NOT_RUN`), también los fallos. Las reglas completas para agentes están en [AGENTS.md](AGENTS.md).
