# Progreso del plan de evolución

Registro vivo del [plan](plan.md). El agente lo actualiza al cerrar cada paso.

## Paso actual

**Fase 1, paso 2: un solo comando de verificación local.** Estado: sin empezar.

## Estado por paso

Estados: `pendiente`, `en curso`, `cerrado`, `bloqueado (⏸ decisión de Tobi)`.

| Paso | Título | Estado | Evidencia |
|---|---|---|---|
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
| 21 | Contrato público por feature, y gate que lo haga cumplir | pendiente | |
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
