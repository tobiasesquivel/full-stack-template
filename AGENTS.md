# full-stack-template

Instrucciones para agentes que trabajan en este repo. `CLAUDE.md` importa este archivo: es la única fuente.

## Qué es

Monorepo template: backend ASP.NET Core 10, frontend Angular 22 con signals y PostgreSQL. La arquitectura es vertical slices dentro de features que funcionan como módulos profundos ([ADR-0004](docs/adr/0004-vertical-slices-deep-modules.md)). Dune (`dune/`) la hace cumplir con analyzers Roslyn y gates Node.

## Para qué existe

Es un laboratorio de aprendizaje. Tobi lleva el backend hasta una arquitectura .NET empresarial completa (patrones, observabilidad, integraciones, mensajería), **un paso por vez y aprendiendo cada concepto**. No alcanza con que funcione: cada paso enseña algo y deja evidencia de que nada se rompió.

El dominio (tareas) es simple a propósito: importa el rigor técnico. **Todo el repo puede cambiar.** El código actual es el punto de partida, no un límite.

## Qué leer

| Archivo | Para qué |
|---|---|
| [docs/evolucion/plan.md](docs/evolucion/plan.md) | Hoja de ruta: Fase 0 (bases) y 10 fases más. Cada paso tiene Concepto, Cambia y Verifica. |
| [docs/evolucion/progreso.md](docs/evolucion/progreso.md) | Paso actual, estado de cada paso y cierres. Leelo primero. |
| [docs/evolucion/informe-vsa-deep-modules.md](docs/evolucion/informe-vsa-deep-modules.md) | Vertical slices + módulos profundos: dónde vive cada pieza y por qué. |
| [docs/evolucion/informe-deep-mediators.md](docs/evolucion/informe-deep-mediators.md) | Módulos profundos (Ousterhout) vs vertical slices, y mediators/buses en .NET. |
| [docs/evolucion/informe-dotnet-empresarial.md](docs/evolucion/informe-dotnet-empresarial.md) | Arquitectura .NET para apps grandes: paquetes, patrones, observabilidad, pagos, colas, sagas, licencias. |
| [CONTEXT.md](CONTEXT.md) | Vocabulario del dominio. Usá esos términos. |
| [docs/architecture.md](docs/architecture.md), [docs/conventions.md](docs/conventions.md) | Arquitectura vigente y reglas para escribir código. |
| [docs/decisions.tsv](docs/decisions.tsv), [docs/adr/](docs/adr/) | Decisiones tomadas y su porqué. |

## Reglas de avance

1. **Un paso = un cambio observable.** No juntes dos pasos.
2. **Ningún paso cierra sin evidencia.** Reportá cada etapa por separado: `PASS`, `FAIL` o `NOT_RUN`. Nunca un "anda" global.
3. **Si un paso cambia un contrato HTTP, va completo:** backend, frontend y tests juntos.
4. **Toda regla nueva se hace ejecutable** en Dune, con una fixture válida y una violación deliberada.
5. **Las decisiones de herramienta o licencia son de Tobi.** Los pasos marcados `⏸ DECISIÓN DE TOBI` se bloquean hasta que elija. No elijas por él.
6. **Cada paso va en su rama y su PR.** Push y PR solo de esa rama. El merge a `main` lo hace Tobi.
7. **No amplíes el alcance del paso.** Si ves algo para mejorar fuera de él, anotalo como pendiente en `progreso.md`.
8. **No adelantes pasos.** Si el plan parece equivocado, discutilo antes de ejecutar.
9. **No cambies un gate, analyzer o test para tapar una falla.** Sí podés cambiarlo si ese cambio es el objetivo declarado del paso: con un ADR o una línea en `docs/decisions.tsv`, y con su fixture válida y su violación deliberada.

## Cómo enseñar

- Un concepto por paso.
- **Antes** de tocar código: qué problema resuelve, cómo está hoy, cómo queda y qué conceptos previos asume.
- **Después**: el resultado con la evidencia. Qué se corrió, qué devolvió y qué significa cada estado.
- Explicá sobre el código real: 5 a 10 renglones con clases y archivos del repo. No uses ejemplos genéricos ni pegues el diff completo.
- Nivel: Tobi conoce C# y Angular. Explicá el *porqué* de cada patrón, no la sintaxis.
- Escribí en español técnico simple: frases cortas, voz activa, primero la conclusión.

## Verificación

La lista única de comandos está en [README.md](README.md#ejecutar-gates). Las pruebas backend necesitan Docker (PostgreSQL real). No hay suite de tests frontend: la prueba real es el navegador y la skill `verify-template`.

La evidencia de cada corrida va en `artifacts/verify-template/<runId>/`. Esa carpeta no se versiona: el resumen de cada paso va en `progreso.md`.

## Al cerrar un paso

1. Actualizá [docs/evolucion/progreso.md](docs/evolucion/progreso.md): estado, evidencia y una entrada en "Cierres".
2. Si tomaste una decisión de diseño, agregala a `docs/decisions.tsv`.
3. Cambiá este archivo solo si cambia una regla.
