# Contexto para retomar el curso del backend

Leé este archivo entero antes de hacer nada. Te deja sabiendo lo mismo que el agente que armó el curso el 2026-11-25.

## Qué es

Un curso para que Tobi entienda el backend de este repo (`full-stack-template`). Lo pidió con la skill `/teach`. Esta carpeta (`docs/aprender/backend/`) es el workspace de esa skill: el directorio donde viven su objetivo, sus notas, sus lecciones y sus registros.

- La rama `t3/teach-backend-repository` existe solo para el curso. No la mergees a `main` ni abras un PR salvo que Tobi lo pida.
- Al terminar cada lección, commiteá y pusheá a esa rama. Así Tobi la baja en cualquier dispositivo.
- La primera versión vivía en `~/.aprender/backend-template/` de otra máquina. Desde ahora, la fuente es esta carpeta.

## Quién es el alumno y qué quiere

- Tobi conoce C# y Angular.
- Programa con agentes: ellos escriben el código y él decide y revisa.
- Objetivo (`MISSION.md`): entender el backend para pedirle bien un cambio a un agente y detectar cuándo el agente se equivoca. Escribir el código él mismo queda fuera.
- Escribí como pide `~/.claude/CLAUDE.md` si existe en tu máquina. Si no existe, este es el resumen:
  - Español rioplatense simple.
  - La primera frase responde lo que pregunta.
  - Cada término técnico se define en la misma frase la primera vez.
  - "=" para equivalencias.
  - Cada pregunta para Tobi sale por la herramienta de preguntas, corta y con opciones.

## Decisiones del formato

Tobi las tomó en una ronda de preguntas el 2026-11-25. Están también en `NOTES.md`.

1. **Orden:** de lo chico a lo grande, en 4 bloques: A (una slice), B (la feature), C (mensajes) y D (procesos). El plan de 14 lecciones está en `reference/mapa.html`.
2. **Tamaño:** una idea por lección, unos 10 minutos.
3. **Formato:** un archivo HTML en `lessons/NNNN-nombre.html` que Tobi abre en su navegador.
   - Abrilo vos al terminar (`start` en Windows, `open` en macOS).
   - Cada página usa `assets/curso.css` y `assets/quiz.js` con rutas relativas.
4. **Ayudas visuales:** al menos un diagrama por lección. Tobi lo pidió: "si en las lecciones podes poner ayudas visuales asi visualizo lo que queres decir mejor".
   - Usá los componentes de `assets/curso.css`: `svg.diagram` (con las clases `node`, `key`, `ext`, `edge`, `back` y `lbl`) y `pre.tree` (con las etiquetas `tag new`, `tag edit` y `tag gen`).
   - Unos 7 nodos como máximo y un verbo en cada flecha.
   - Copiá el estilo del diagrama de la lección 1.
5. **Porqué:** de 2 a 3 líneas sobre por qué el repo lo hace así, y 1 línea sobre qué hace la industria. Las alternativas, solo si Tobi las pide.
6. **Práctica:** solo quiz, sin ejercicios de código.
   - Mezclá dos tipos de pregunta: "Recordar" y "Revisar a un agente". La segunda muestra un cambio inventado de un agente con un error.
   - Cada opción tiene la misma cantidad de palabras.
   - La respuesta correcta cambia de posición entre preguntas.
   - Marcado del quiz: `<div class="quiz" data-answer="N" data-id="P1">`, con `<p class="tipo">`, `<p class="q">`, `<div class="opts">` (botones) y `<p class="why">`.
7. **Sección fija:** "Qué mirar cuando un agente toca esto", con 2 o 3 puntos. No hay una hoja de revisión aparte: Tobi dijo que no.
8. **Repaso:** desde la lección 2, cada lección empieza con 2 preguntas de lecciones anteriores.
   - Elegilas por lo que Tobi falló.
   - Si no falló nada, elegí las del tipo "Revisar a un agente".
   - Ponele a cada una un id como `R-L1-P2`.
9. **Resultado:** `quiz.js` muestra al final una línea para copiar, por ejemplo `Lección 1 (recorrido del pedido): 4 de 5. Fallé: P2.`. `<main data-lesson="...">` define el nombre de la lección en esa línea.
   - Cuando Tobi te la pega, anotá en `~/.aprender/registro.md` una fila por concepto, si ese archivo existe en tu máquina.
   - Si no existe, anotala en `registro.md` dentro de esta carpeta.
   - Columnas: Concepto | Estado | Último repaso | Próximo repaso | Proyecto | Fuente | Nota.
10. **Ritmo:** una lección por vez. La siguiente se arma cuando Tobi pega el resultado de la anterior.
11. **Cierre de cada lección:** la fuente principal recomendada, una invitación a preguntar y la navegación al mapa.

## Estado al 2026-11-25

- Lección 1 (`lessons/0001-el-recorrido-de-un-pedido.html`): resultado 5 de 5, sin fallas. Filas en `registro.md` de esta carpeta (en la notebook no existe `~/.aprender/registro.md`) y registro `learning-records/0003`.
- Lección 2 (`lessons/0002-los-archivos-de-una-slice.html`): resultado 6 de 7. Falló P5: un `if` en el endpoint que compara `CallerId` con `authId` deja afuera al Admin.
- Lección 3 (`lessons/0003-registro-y-codegen.html`): resultado 6 de 7. Falló R-L2-P5, el mismo concepto de permisos, por segunda vez.
- Por eso se adelantó permisos: ahora es la lección 4 y errores pasó a la 5 (el mapa ya está cambiado).
- Lección 4 entregada: `lessons/0004-permisos-duenio-o-permiso.html`. Enseña las dos preguntas de `CanAccessAsync` (`AccessPolicy.cs:37-47`), la tabla de roles y permisos (`RolePermissions.cs:86-113`), y una tabla que compara el `if` del endpoint con `CanAccessAsync`. Repaso R-L3-P4 y R-L2-P4, y 5 preguntas. P2 y P5 atacan el error de Tobi con formas nuevas. En total son 7.
- **Esperando:** que Tobi pegue el resultado de la lección 4. Si vuelve a fallar P2 o P5, preguntale qué opción eligió y por qué, antes de seguir.
- No probado en ninguna máquina: qué excepción tira `InvokeAsync` si falta `AddMediatorHandler`, y en qué momento falla el modo `Static` si falta el código generado. La lección 3 no lo afirma. La notebook no puede probarlo: le falta el SDK 10.0.401 que pide `global.json` y Docker no estaba corriendo.
- El mapa del curso ya responde corto todas las preguntas iniciales de Tobi: closure, `ClosureOwner`, `IOwnedResource`, `AccountClosureFacts`, `MessageNames`, orquestar, saga, RabbitMQ, `Hosts/` e `Infrastructure/`. Las lecciones las profundizan.
- `Infrastructure/` queda como caja negra hasta las lecciones 10 a 13.

## Próximas lecciones

Estos datos ya están verificados contra el código. Volvé a confirmar cada número de línea antes de citarlo, porque el repo cambia.

**Lección 5, errores (`ErrorOr` y `ApiResults`):** empezá el repaso con una pregunta de permisos (R-L4-…). Leé `Shared/Http/ApiResults.cs` y `Features/Users/UserErrors.cs` antes de armarla. La lección 1 ya mostró la traducción básica (valor 200, `Forbidden` 403, `NotFound` 404).

**Lecciones 6 a 14:** están en el mapa. Para cada una:
1. Leé el código.
2. Leé la investigación del paso en `docs/evolucion/investigacion/`: 27 para la saga, 25 para el broker, 28 para el período de gracia y 23-2 para Hosts.
3. Sumá fuentes de `RESOURCES.md`.

## Reglas de la skill `/teach`

Si la skill no está instalada en tu máquina, seguí estas reglas:
- Cada lección se ata a `MISSION.md`.
- Afirmá solo lo que sale del código o de una fuente con link. No confíes en lo que recordás.
- Escribí un registro en `learning-records/NNNN-nombre.md` solo cuando Tobi muestre que entendió algo, cuando cuente algo que ya sabía o cuando se corrija un error suyo. Cubrir un tema no alcanza.
- Un glosario en `reference/` se crea solo con términos que Tobi ya usa bien. Todavía no existe.
- Cada lección recomienda una fuente principal y recuerda que Tobi puede preguntar.

## Archivos de esta carpeta

| Archivo | Para qué |
|---|---|
| `MISSION.md` | Objetivo del curso. |
| `NOTES.md` | Decisiones de formato y notas del docente. |
| `RESOURCES.md` | Fuentes del repo y externas, con para qué sirve cada una. |
| `learning-records/` | Lo que Tobi ya sabe o cambió (0001: punto de partida; 0002: dirigir y revisar agentes; 0003: sigue el recorrido de un pedido). |
| `registro.md` | Registro de repaso con evidencia del quiz, cuando la máquina no tiene `~/.aprender/registro.md`. |
| `reference/mapa.html` | Plan de 14 lecciones y respuestas cortas a las preguntas iniciales. |
| `lessons/` | Las lecciones. |
| `assets/curso.css`, `assets/quiz.js` | Componentes compartidos. Reusalos y no copies código entre lecciones. |
| `assets/inline.mjs` | `node assets/inline.mjs <lección.html> <salida.html>` arma una copia con el CSS y el JS adentro, para previsualizarla en una herramienta que no resuelve rutas relativas. |
