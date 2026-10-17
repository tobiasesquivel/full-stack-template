# Progreso del plan de evolución

Registro vivo del [plan](plan.md). El agente lo actualiza al cerrar cada paso.

## Paso actual

**Fase 0, paso 0.2: arreglos de Dune.** Estado: sin empezar.

La Fase 0 va antes del paso 2. El paso 1 (línea base) ya cerró y sirve de referencia para la Fase 0.

## Estado por paso

Estados: `pendiente`, `en curso`, `cerrado`, `bloqueado (⏸ decisión de Tobi)`.

| Paso | Título | Estado | Evidencia |
|---|---|---|---|
| 0.1 | Docs y reglas de trabajo | cerrado | `artifacts/verify-template/fase0-01-docs/` |
| 0.2 | Arreglos de Dune | pendiente | |
| 0.3 | Reparto del código | pendiente | |
| 0.4 | Slices | pendiente | |
| 0.5 | Límite entre Auth y Users | pendiente | |
| 0.6 | Frontend por feature | pendiente | |
| 1 | Línea base: qué funciona hoy, con evidencia | cerrado | [linea-base.md](linea-base.md); `artifacts/verify-template/baseline-20261016/` |
| 2 | Un solo comando de verificación local | pendiente | |
| 3 | CI verde de verdad | pendiente | |
| 4 | Versionado de API declarado | pendiente | |
| 5 | Validación de configuración al arranque | pendiente | |
| 6 | Catálogo de errores estable | pendiente | |
| 7 | Paginación por keyset | pendiente | |
| 8 | Concurrencia optimista en `TaskItem` | pendiente | |
| 9 | Idempotencia de escritura | pendiente | |
| 10 | Auditoría de cambios | pendiente | |
| 11 | Permisos memorizados por request | pendiente | |
| 12 | Rate limiting por usuario y por tipo de endpoint | pendiente | |
| 13 | Cabeceras de seguridad y escaneo continuo | pendiente | |
| 14 | Métricas de negocio reales | pendiente | |
| 15 | Correlación log↔traza automatizada | pendiente | |
| 16 | Health por dependencia | pendiente | |
| 17 | SLOs y runbooks | pendiente | |
| 18 | Índices medidos, no supuestos | pendiente | |
| 19 | Migraciones expand/contract ensayadas | pendiente | |
| 20 | Lectura dedicada por módulo (CQRS liviano) | pendiente | |
| 21 | Contrato público por feature, y gate que lo haga cumplir | pendiente (gran parte absorbida por 0.5) | |
| 22 | Un `DbContext` por feature | pendiente | |
| 23 | Extraer un servicio con gateway adelante | pendiente | |
| 24 | Eventos de dominio + outbox transaccional, sin broker | pendiente | |
| 25 | Broker real con consumidor idempotente y DLQ | pendiente | |
| 26 | Saga coreografiada (3 pasos con compensación) | pendiente | |
| 27 | Saga orquestada con máquina de estados persistida | pendiente | |
| 28 | Durable execution para un proceso largo con auditoría | pendiente | |
| 29 | Primer cliente HTTP saliente con resiliencia | pendiente | |
| 30 | Pasarela de pago detrás de un puerto propio | pendiente | |
| 31 | Máquina de estados de pago + conciliación programada | pendiente | |
| 32 | Integración por archivo (SFTP o CSV) con cuarentena | pendiente | |
| 33 | Caché distribuida con invalidación medida | pendiente | |
| 34 | Cadena de suministro | pendiente | |
| 35 | Despliegue progresivo con rollback probado | pendiente | |
| 36 | Gobernanza final | pendiente | |

## Pendientes fuera de alcance

Cosas vistas durante un paso que no le corresponden. Cada una espera su paso.

- Catálogo de errores: `TaskErrors`, `UserErrors` y `AuthErrors` usan prefijos distintos y hay errores escritos en línea (`"Task.Forbidden"` aparece 4 veces). Paso 6.
- CI falla en el job de backend desde el primer run en GitHub: 9 tests de `TasksTests` caen con `Failed to seed permission ... Optimistic concurrency failure`. Los roles se siembran dos veces en paralelo: `ApiFixture.cs:87` y `DatabaseInitializer.cs:17`, que corre al arrancar la API. En local pasó (28/28); en CI la carrera aparece. Paso 3.
- `backend/AGENTS.md` y `backend/docs/agents/` describen un issue tracker en `.scratch/`, que `.gitignore` excluye: los issues nunca se versionan.

## Cierres

Una entrada por paso cerrado, con este formato:

```md
### Paso N — AAAA-MM-DD
- Enseñado: qué concepto quedó claro.
- Preguntas: qué preguntó Tobi.
- Evidencia: artifacts/verify-template/<runId>/
```

### Paso 1 — 2026-10-16
- Enseñado: línea base; "compila" no es "funciona"; PASS/FAIL/NOT_RUN; verificación reproducible con run ID y evidencia que sobrevive al cleanup.
- Preguntas: qué concepto enseña el paso.
- Evidencia: artifacts/verify-template/baseline-20261016/ y [linea-base.md](linea-base.md).

### Paso 0.1 — 2026-10-16
- Enseñado: una sola fuente de verdad por regla; las decisiones van en un ADR con alternativas y consecuencias.
- Preguntas: si la arquitectura ya estaba definida; si una clase por slice es compatible con los módulos profundos de Ousterhout; dónde va el código compartido.
- Decisiones: ADR-0004 y `docs/decisions.tsv`.
- Evidencia: artifacts/verify-template/fase0-01-docs/. Links relativos: PASS (57 links en 27 archivos, 0 rotos). Referencias viejas: PASS (12 coincidencias, todas intencionales: ADR-0004 nombra lo que reemplaza, `informe-deep-mediators.md` conserva su texto con nota). `check-structure`: PASS (14/14). `check-comments`: PASS (12/12). Build y tests .NET: NOT_RUN, el paso no toca código.
