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

## Cómo enseñar

- Un concepto por paso.
- **Antes** de tocar código: qué problema resuelve, cómo está hoy, cómo queda y qué conceptos previos asume.
- **Después**: el resultado con la evidencia. Qué se corrió, qué devolvió y qué significa cada estado.
- Explicá sobre el código real: 5 a 10 renglones con clases y archivos del repo. No uses ejemplos genéricos ni pegues el diff completo.
- Nivel: Tobi conoce C# y Angular. Explicá el *porqué* de cada patrón, no la sintaxis.
- Escribí en español técnico simple: frases cortas, voz activa, primero la conclusión.

### Cuando Tobi quiere aprender algo

Si Tobi dice "enseñame", "quiero entender" o "explicámelo", usá la skill `/aprender` si está disponible. Si no, seguí su método:

1. **Elegí 1 a 3 ideas.** La sesión enseña esas ideas, no el tema entero.
2. **Enseñá desde la fuente:** el código con `archivo:línea` o la documentación oficial con su link.
3. **Una idea por vez:** respuesta corta, nombre y definición, mecanismo paso a paso y un ejemplo del repo.
4. **Elegí el medio más simple:**
   - Texto para una definición o una decisión.
   - Mermaid para un flujo o una estructura. Armalo de a poco: cada diagrama agrega una parte, con unos 7 elementos como máximo y un verbo en cada flecha.
   - Página HTML para lo que cambia: un antes y después, un valor que se mueve, un sistema para explorar. En T3 Code, revisala con `html_preview` y publicala en el chat con `html_render`. Fuera de T3, guardala en `~/.aprender/lecciones/` y abrila.
5. **Cerrá cada idea con 1 a 3 preguntas** que Tobi responda de memoria: predecir qué pasa, explicar una línea con sus palabras, decir dónde cambiaría el código. Esperá la respuesta y dale feedback concreto antes de seguir.
6. **Ajustá la ayuda:** si acierta rápido, menos ejemplo y preguntas más difíciles; si se traba, otro ejemplo resuelto.

## Verificación

La lista única de comandos está en [README.md](README.md#ejecutar-gates). Las pruebas backend necesitan Docker (PostgreSQL real). No hay suite de tests frontend: la prueba real es el navegador y la skill `verify-template`.

La evidencia de cada corrida va en `artifacts/verify-template/<runId>/`. Esa carpeta no se versiona: el resumen de cada paso va en `progreso.md`.

## Al cerrar un paso

1. Actualizá [docs/evolucion/progreso.md](docs/evolucion/progreso.md): estado, evidencia y una entrada en "Cierres".
2. Si tomaste una decisión de diseño, agregala a `docs/decisions.tsv`.
3. Cambiá este archivo solo si cambia una regla.
