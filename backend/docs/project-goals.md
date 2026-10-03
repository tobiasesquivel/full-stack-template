# Project Goals — Professional API Maturity

> Independientes del dominio de negocio. Cada meta es un concepto presente en APIs profesionales
> que este template aún no implementa.

---

## 1. Observability

| Meta | Estado | Descripción |
|---|---|---|
| Request logging | ❌ | `app.UseSerilogRequestLogging()` para loguear cada request HTTP con latencia y status |
| Correlation ID | ❌ | Header `X-Correlation-Id` propagado a logs y ProblemDetails. Middleware que genera uno si no existe |
| Structured log enrichment | ❌ | Enriquecer logs con `RequestId`, `UserId`, `CorrelationId` automáticamente |
| Real health checks | ❌ | `AddHealthChecks()` con probes para DB (Npgsql), Identity, Wolverine. Reemplazar el endpoint falso actual |
| OpenTelemetry metrics | ❌ | Métricas de latencia, errores, throughput exportables a Prometheus / OTLP |

## 2. Reliability

| Meta | Estado | Descripción |
|---|---|---|
| Rate limiting | ❌ | Fixed-window sobre `/api/v1/auth/*` (register, login, refresh). ASP.NET Core `AddRateLimiter()` |
| Background jobs | ❌ | Usar Wolverine para jobs diferidos: email confirmation, cleanup de refresh tokens expirados |
| Graceful shutdown | ❌ | `IHostApplicationLifetime` para drenar requests en vuelo antes de caer |
| Retry / resilience | ❌ | Polly para llamadas externas (si se agregan). Circuit breaker + retry con backoff |

## 3. Security

| Meta | Estado | Descripción |
|---|---|---|
| Secrets management | ❌ | Migrar de valores hardcodeados a User Secrets (dev) + variables de entorno (prod). Eliminar secrets de `appsettings.json` |
| CORS | ❌ | Política explícita — solo orígenes confiables. No `AllowAnyOrigin` |
| Security headers | ❌ | HSTS, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` |
| Email confirmation | ❌ | Flujo completo: token → email → confirmación. Identity ya tiene `DefaultTokenProviders` configurados |
| API keys (opcional) | ❌ | Autenticación server-to-server como alternativa a JWT para integraciones |

## 4. Developer Experience

| Meta | Estado | Descripción |
|---|---|---|
| API versioning | ❌ | `Asp.Versioning` con URL path versioning. Preparar `/api/v2/` sin romper `/api/v1/` |
| Pagination | ❌ | Offset-based (mínimo viable). Response con `items`, `page`, `pageSize`, `totalCount`, `hasNext` |
| Filtering & sorting | ❌ | Query params tipados: `?status=done&sort=-createdAt`. Validación de campos sortables |
| OpenAPI annotations | ❌ | `[ProducesResponseType]`, XML docs en endpoints, ejemplos de request/response en Scalar |
| Response compression | ❌ | Brotli + GZip middleware para payloads > 1KB |
| Problem Details RFC 9457 | ❌ | `type` URI, `instance`, `traceId` (correlation ID) en todos los errores. Extender el `ApiController.Problem()` actual |

## 5. Infrastructure

| Meta | Estado | Descripción |
|---|---|---|
| Docker Compose | ❌ | `docker-compose.yml` con API + PostgreSQL. One-command dev environment |
| CI/CD pipeline | ❌ | GitHub Actions: build, test, lint. Al menos build + test en PR |
| Tests | ❌ | xUnit + test project. Unitarios (handlers con EF in-memory), integración (WebApplicationFactory + PostgreSQL en Docker) |
| Feature flags | ❌ | Sistema simple para deployar código sin activar features (config-based, sin servicio externo) |
| Database migration strategy | ❌ | Auto-migrate en dev, script SQL generado en CI para prod |

---

## Priorización sugerida

### Fase 1 — Base profesional mínima
Secrets management → CORS → Request logging → Docker Compose → CI/CD (build + test) → Email confirmation

### Fase 2 — API madura
Pagination → Filtering & sorting → API versioning → Rate limiting → Real health checks → Problem Details enriquecidos

### Fase 3 — Producción
Correlation ID → OpenTelemetry → Background jobs → Security headers → Response compression → Feature flags

### Fase 4 — Excelencia
Tests → Graceful shutdown → API keys → Retry/resilience

---

*Última actualización: 2026-06-17*
