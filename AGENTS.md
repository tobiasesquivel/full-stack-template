# full-stack-template

Instrucciones para agentes que trabajan en este repo. `CLAUDE.md` importa este archivo: es la única fuente.

## Qué es

Monorepo template: backend ASP.NET Core 10, frontend Angular 22 con signals y PostgreSQL. La arquitectura es vertical slices dentro de features que funcionan como módulos profundos ([ADR-0004](docs/adr/0004-vertical-slices-deep-modules.md)). Dune (`dune/`) la controla con analyzers Roslyn y gates Node; está en desarrollo.

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
| [docs/evolucion/informe-angular-moderno.md](docs/evolucion/informe-angular-moderno.md) | Angular v20-v22: nombres sin sufijo, `@Service()`, Signal Forms y la skill oficial `angular-developer`. |
| [docs/evolucion/informe-sesion-angular.md](docs/evolucion/informe-sesion-angular.md) | Sesión en Angular: estado con signals, guards, interceptor con renovación, varias pestañas y BFF frente a token en memoria. |
| [docs/evolucion/informe-componentes-angular.md](docs/evolucion/informe-componentes-angular.md) | Componentes ruteados y presentacionales en Angular v22: cuándo separar, cómo fluye el estado con signals y qué conviene en `Tasks`. |
| [docs/evolucion/informe-frontend-para-agentes.md](docs/evolucion/informe-frontend-para-agentes.md) | Qué hace que un frontend sea fácil para agentes de código, comparado con lo que ya tiene el repo, y los huecos por prioridad. |
| [docs/evolucion/investigacion/](docs/evolucion/investigacion/) | Cómo se implementa cada paso de forma profesional, con fuentes (regla 12). |
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
10. **Los docs deciden el diseño; Dune los sigue.** La fuente de verdad es ADR-0004, `architecture.md`, `conventions.md` y los informes de `docs/evolucion/`. Justificá cada decisión de diseño con ellos. Si una regla de Dune choca con los docs, corregí la regla en el mismo paso, con lo que pide la regla 9. Si dos docs se contradicen, frená y preguntale a Tobi.
11. **Un paso cierra cuando Tobi entendió su concepto, no cuando los gates dan verde.** Cada paso para dos veces y espera la respuesta de Tobi: después de la explicación, antes del código, y después de la evidencia, con el concepto explicado sobre el código que cambió. Hasta que Tobi responde, el paso queda `en curso`.
12. **Antes de explicar un paso que cambia código, investigá cómo se implementa de forma profesional.** El plan dice qué pieza entra; la investigación dice cómo queda bien en este código. Delegala a un subagente con la skill `research`: fuentes primarias (documentación oficial, repos y guías de los mantenedores), con un link por afirmación, y dentro de la arquitectura del repo (ADR-0004, `architecture.md`, `conventions.md`). El resultado va en `docs/evolucion/investigacion/paso-<N>-<tema>.md`. Contrastá cada afirmación con el código antes de usarla, y apoyá la explicación previa en ese archivo. Si la investigación choca con los docs del repo, frená y preguntale a Tobi.

## Cómo enseñar

- Un concepto por paso.
- **Antes** de tocar código: qué problema resuelve, cómo está hoy, cómo queda y qué conceptos previos asume. Terminá ese mensaje sin código y esperá que Tobi responda: puede preguntar, corregir o elegir.
- **Después**: el resultado con la evidencia. Qué se corrió, qué devolvió y qué significa cada estado. Explicá el concepto del paso sobre el código que cambió y esperá que Tobi responda.
- Explicá sobre el código real: 5 a 10 renglones con clases y archivos del repo. No uses ejemplos genéricos ni pegues el diff completo.
- Nivel: Tobi conoce C# y Angular. Explicá el *porqué* de cada patrón, no la sintaxis.
- Escribí en español técnico simple: frases cortas, voz activa, primero la conclusión.

### Cuando Tobi quiere aprender algo

Si Tobi dice "enseñame", "quiero entender" o "explicámelo", seguí este método. Enseñá sin preguntarle para comprobar: Tobi pregunta lo que necesite.

1. **Elegí 1 a 3 ideas.** La sesión enseña esas ideas, no el tema entero.
2. **Enseñá desde la fuente:** el código con `archivo:línea` o la documentación oficial con su link.
3. **Una idea por vez:** respuesta corta, nombre y definición, mecanismo paso a paso y un ejemplo del repo.
4. **Elegí el medio más simple:**
   - Texto para una definición o una decisión.
   - Mermaid para un flujo o una estructura. Armalo de a poco: cada diagrama agrega una parte, con unos 7 elementos como máximo y un verbo en cada flecha.
   - Página HTML para lo que cambia: un antes y después, un valor que se mueve, un sistema para explorar. En T3 Code, revisala con `html_preview` y publicala en el chat con `html_render`. Fuera de T3, guardala en `~/.aprender/lecciones/` y abrila.

## Verificación

La lista única de comandos está en [README.md](README.md#ejecutar-gates). Las pruebas backend necesitan Docker (PostgreSQL real). El frontend tiene una suite de Vitest con jsdom (`npm test -- --watch=false` en `frontend/`) para funciones puras y componentes de presentación. El recorrido completo se prueba en el navegador con la skill `verify-template`.

`.mcp.json` declara el MCP del CLI de Angular en solo lectura (`ng mcp --read-only --root frontend`, con el CLI instalado en `frontend/node_modules`). Da `search_documentation` y `get_best_practices` de la versión instalada y `list_projects`; no ejecuta builds ni el dev server. Claude Code lo pide aprobar la primera vez. Necesita `npm ci` en `frontend/`.

La evidencia de cada corrida va en `artifacts/verify-template/<runId>/`. Esa carpeta no se versiona: el resumen de cada paso va en `progreso.md`.

## Al cerrar un paso

1. Actualizá [docs/evolucion/progreso.md](docs/evolucion/progreso.md): estado, evidencia y una entrada en "Cierres". La entrada se escribe después de la respuesta de Tobi: `Enseñado` nombra lo que explicaste y `Preguntas` registra lo que Tobi preguntó. El PR puede abrirse antes, con la evidencia.
2. Si tomaste una decisión de diseño, agregala a `docs/decisions.tsv`.
3. Cambiá este archivo solo si cambia una regla.
