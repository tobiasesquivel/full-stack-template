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
- Lección 4 (`lessons/0004-permisos-duenio-o-permiso.html`): 5 de 7. Acertó P2 y P5, así que el error de permisos quedó corregido (`learning-records/0004`). Falló R-L3-P4 (sacar un parámetro del constructor sin `codegen write` también rompe el build) y P4 (`Read` frente a `Manage`).
- Entre las lecciones 4 y 5 Tobi preguntó qué es el "caller" y qué es "closure". No sabía qué era `CallerId`: desde la lección 5, escribilo siempre "`CallerId` (quién pide)". Closure = cierre de cuenta (`CONTEXT.md:27`), no el closure de C#.
- Lección 5 (`lessons/0005-errores-erroror-y-apiresults.html`): 5 de 7. Falló R-L4-P4 (roles: Admin no tiene `Manage`), que es la segunda vez, y P5 (un `code` publicado no cambia). Le pregunté qué opción eligió en las dos: no se acuerda.
- Lección 6 (`lessons/0006-request-command-y-validador.html`): Tobi dijo "hice bien la lección" sin pegar la línea; quedó anotada como 7 de 7. Acertó los repasos de roles y del `code`, así que los dos volvieron a "sabido".
- Lección 7 (`lessons/0007-transacciones-del-mediator.html`): 4 de 7. Falló R-L6-P2 (permisos, otra vez), P2 (el handler no guarda) y P4 (`None` guarda aunque haya error). Le ofrecí un refuerzo corto y eligió seguir con la 8.
- Patrón: Tobi acierta las preguntas de "Recordar" y falla las de "Revisar a un agente" cuando la consecuencia es indirecta. Las explicaciones de la lección 8 en adelante abren con una regla corta en negrita, y los repasos la repiten.
- Lección 8 (`lessons/0008-la-feature-por-dentro.html`): 5 de 7. Acertó los dos repasos de transacciones. Falló P3 (regla compartida → `Domain/`) y P4 (permisos, por cuarta vez: una ruta con `{authId}` necesita `CanAccessAsync`). Con permisos acierta solo si la pregunta nombra el permiso; la lección 9 le da un reflejo para revisar ("si la ruta trae el id de algo con dueño…").
- Lección 9 (`lessons/0009-el-cierre-de-cuenta-y-sus-facts.html`): Tobi dijo "todo bien" sin pegar la línea; quedó como 7 de 7.
- Lección 10 (`lessons/0010-mensajes-mediator-y-broker.html`): 6 de 7. Acertó los dos repasos: permisos (segunda vez seguida con el reflejo, pasó a "sabido") y transacciones con historial. Falló P4, de "Recordar": quién espera la respuesta (`InvokeAsync` sí; un comando por el broker no).
- Lección 11 entregada: `lessons/0011-rabbitmq-exchange-cola-binding.html` (exchange, clave de ruteo, cola, binding y DLQ con los nombres reales; por qué el mensaje sobrevive en tres tramos: outbox, cola quorum, ack al terminar; cuándo se pierde: sin binding). Repaso R-L10-P4 y R-L10-P4b, los dos sobre quién espera. En total son 7.
- Lección 11: Tobi dijo "todo bien sigamos" sin pegar la línea; quedó como 7 de 7. Quién espera la respuesta pasó a "sabido".
- Lección 12 entregada: `lessons/0012-la-saga-orquestada.html` (los cinco estados de `AccountClosureSaga`, cómo se lee un `Handle`: identidad, guarda `TryMove` y comando devuelto; el código generado de la saga; pivote al final y compensación en orden inverso). Repaso R-L11-P4b (orden de despliegue) y R-L11-P3b (la respuesta espera en `users.account-closures`). En total son 7.
- Lección 12: Tobi dijo "entendí todo" sin pegar la línea; quedó como 7 de 7.
- Lección 13 entregada: `lessons/0013-el-periodo-de-gracia.html` (la saga `AccountClosureGracePeriod`, los tres `TimeoutMessage`, todo en la transacción de `DeleteUser` y por eso `Eager`, cancelar borra la fila y los plazos caen en `NotFound`, la carrera con `FOR UPDATE`). Repaso R-L4-P2b (permisos espaciado: cancelar un cierre necesita `CanAccessAsync`) y R-L12-P3b (la saga no llama por HTTP). En total son 7.
- **Esperando:** el resultado de la lección 13. Si falla R-L4-P2b, permisos vuelve a "en repaso".
- Se corrigió una cita de la lección 4: `RolePermissions.cs:86-113` era `:10-37`. Ojo con `cat -n` de varios archivos juntos: numera seguido y da líneas falsas.
- No probado en ninguna máquina: qué excepción tira `InvokeAsync` si falta `AddMediatorHandler`, y en qué momento falla el modo `Static` si falta el código generado. La lección 3 no lo afirma. La notebook no puede probarlo: le falta el SDK 10.0.401 que pide `global.json` y Docker no estaba corriendo.
- El mapa del curso ya responde corto todas las preguntas iniciales de Tobi: closure, `ClosureOwner`, `IOwnedResource`, `AccountClosureFacts`, `MessageNames`, orquestar, saga, RabbitMQ, `Hosts/` e `Infrastructure/`. Las lecciones las profundizan.
- `Infrastructure/` queda como caja negra hasta las lecciones 10 a 13.

## Próximas lecciones

Estos datos ya están verificados contra el código. Volvé a confirmar cada número de línea antes de citarlo, porque el repo cambia.

**Lección 14, Hosts (dos programas):** por qué hay `Hosts/Monolith` y `Hosts/Tasks`. Leé `docs/evolucion/investigacion/paso-23-2-extraer-tasks.md`, `docs/architecture.md` y los `Program.cs` de cada host. Es la última del mapa: cerrá con un repaso de lo que más le costó (permisos y transacciones) y preguntale a Tobi cómo sigue.

**Lección 13 (ya entregada), el período de gracia:** `StartAccountClosure` abre la saga (`Users/StartAccountClosure/StartAccountClosure.cs:48` llama a `AccountClosureSaga.Open`). Fuentes: `docs/architecture.md`, subsección "Período de gracia e historial", y `docs/evolucion/investigacion/paso-28-*`. El plazo `AccountClosureOverdue` (`AccountClosureSaga.cs:80-91`) y los mensajes programados de Wolverine sirven para explicar cómo se espera días sin memoria. Código generado de la saga ya verificado: `OwnerTasksHeldIntegrationEventHandler467166336.cs` líneas 43 (transacción), 58 (`FindAsync`), 63 (`NotFound`), 72 (`Handle`), 76 (`EnqueueCascadingAsync`), 89 (`Version`), 96 (guarda) y 116 (confirma).

**Lecciones 12 a 14:** están en el mapa. Para cada una:
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
| `learning-records/` | Lo que Tobi ya sabe o cambió (0001: punto de partida; 0002: dirigir y revisar agentes; 0003: sigue el recorrido de un pedido; 0004: permiso = dueño o permiso). |
| `registro.md` | Registro de repaso con evidencia del quiz, cuando la máquina no tiene `~/.aprender/registro.md`. |
| `reference/mapa.html` | Plan de 14 lecciones y respuestas cortas a las preguntas iniciales. |
| `lessons/` | Las lecciones. |
| `assets/curso.css`, `assets/quiz.js` | Componentes compartidos. Reusalos y no copies código entre lecciones. |
| `assets/inline.mjs` | `node assets/inline.mjs <lección.html> <salida.html>` arma una copia con el CSS y el JS adentro, para previsualizarla en una herramienta que no resuelve rutas relativas. |
