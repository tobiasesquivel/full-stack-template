# full-stack-template

Instrucciones para agentes que trabajan en este repo. `CLAUDE.md` importa este archivo: es la única fuente.

## Qué es

Monorepo template: backend ASP.NET Core 10 por feature (Auth, Users, Tasks, Health), frontend Angular 22 con signals y PostgreSQL. Los módulos se llaman directo, sin mediator ni bus. Dune (`dune/`) hace cumplir la arquitectura con analyzers Roslyn y gates Node.

## Para qué existe

Es un laboratorio de aprendizaje. Tobi lleva el backend hasta una arquitectura .NET empresarial completa (patrones, observabilidad, integraciones, mensajería), **un paso por vez y aprendiendo cada concepto**. No alcanza con que funcione: cada paso enseña algo y deja evidencia de que nada se rompió.

## Qué leer

| Archivo | Para qué |
|---|---|
| [docs/evolucion/plan.md](docs/evolucion/plan.md) | Hoja de ruta: 10 fases, 36 pasos. Cada paso tiene Concepto, Cambia y Verifica. |
| [docs/evolucion/progreso.md](docs/evolucion/progreso.md) | Paso actual, estado de cada paso y cierres. Leelo primero. |
| [docs/evolucion/informe-deep-mediators.md](docs/evolucion/informe-deep-mediators.md) | Módulos profundos (Ousterhout) vs vertical slices, y mediators/buses en .NET. |
| [docs/evolucion/informe-dotnet-empresarial.md](docs/evolucion/informe-dotnet-empresarial.md) | Arquitectura .NET para apps grandes: paquetes, patrones, observabilidad, pagos, colas, sagas, licencias. |
| [CONTEXT.md](CONTEXT.md) | Vocabulario del dominio. Usá esos términos. |
| [docs/architecture.md](docs/architecture.md), [docs/conventions.md](docs/conventions.md) | Arquitectura vigente y reglas de contribución. |
| [docs/decisions.tsv](docs/decisions.tsv), [docs/adr/](docs/adr/) | Decisiones tomadas y su porqué. |

## Reglas de avance

1. **Un paso = un cambio observable.** No juntes dos pasos.
2. **Ningún paso cierra sin evidencia.** Reportá cada etapa por separado: `PASS`, `FAIL` o `NOT_RUN`. Nunca un "anda" global.
3. **Si un paso cambia un contrato HTTP, va completo:** backend, frontend y tests juntos.
4. **Toda regla nueva se hace ejecutable** en Dune, con una fixture válida y una violación deliberada.
5. **Las decisiones de herramienta o licencia son de Tobi.** Los pasos marcados `⏸ DECISIÓN DE TOBI` se bloquean hasta que elija. No elijas por él.
6. **Sin PR, push ni merge a `main`** salvo pedido explícito.
7. **No toques lo que funciona** si el paso no lo pide.
8. **No adelantes pasos.** Si el plan parece equivocado, discutilo antes de ejecutar.
9. **No toques un gate, analyzer o test para que pase.**

## Cómo enseñar

- Un concepto por paso.
- **Antes** de tocar código: qué problema resuelve, cómo está hoy, cómo queda y qué conceptos previos asume.
- **Después**: el resultado con la evidencia. Qué se corrió, qué devolvió y qué significa cada estado.
- Explicá sobre el código real: 5 a 10 renglones con clases y archivos del repo. No uses ejemplos genéricos ni pegues el diff completo.
- Nivel: Tobi conoce C# y Angular. Explicá el *porqué* de cada patrón, no la sintaxis.
- Escribí en español técnico simple: frases cortas, voz activa, primero la conclusión.

## Verificación

Los comandos están en [README.md](README.md#ejecutar-gates). Desde la raíz:

```sh
dotnet build TemplateApp.slnx --configuration Release
dotnet test TemplateApp.slnx --configuration Release   # necesita Docker (PostgreSQL real)
dotnet run --project dune/Dune.CommentVerifier -- --root .
node dune/check-structure.mjs && node --test dune/check-structure.test.mjs
node dune/check-comments.mjs --root . && node --test dune/check-comments.test.mjs
```

Frontend, desde `frontend/`: `npm ci`, `npm run build` y `node ../dune/check-frontend.mjs --root ..`. No hay suite de tests frontend: la prueba real es el navegador y `.agents/skills/verify-template/scripts/smoke-api.mjs`.

La evidencia de cada corrida va en `artifacts/verify-template/<runId>/`.

## Al cerrar un paso

1. Actualizá [docs/evolucion/progreso.md](docs/evolucion/progreso.md): estado, evidencia y una entrada en "Cierres".
2. Si tomaste una decisión de diseño, agregala a `docs/decisions.tsv`.
3. Cambiá este archivo solo si cambia una regla.
