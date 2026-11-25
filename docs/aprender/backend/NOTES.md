# Notas del docente

## Decisiones del formato (grilling del 2026-11-25)
- Orden: de lo chico a lo grande. Tobi delegó la elección ("te sigo a vos"); el mapa del curso cubre la vista general.
- Una idea por lección, unos 10 minutos.
- HTML que Tobi abre en su navegador. Abrirlo con `start` al terminar cada lección; no mostrarlo con `html_render`.
- Al menos un diagrama por lección (Tobi, 2026-11-25: "si en las lecciones podes poner ayudas visuales asi visualizo lo que queres decir mejor"). Componentes `svg.diagram` y `pre.tree` de `assets/curso.css`; unos 7 nodos como máximo y un verbo en cada flecha.
- Porqué: 2 o 3 líneas de por qué el repo lo hace así, más 1 línea de qué hace la industria. Alternativas solo si las pide.
- Práctica: solo quiz, sin ejercicios de código. Mezcla de preguntas "Recordar" y "Revisar a un agente" (un cambio inventado de un agente con un error).
- Repaso: desde la lección 2, cada lección empieza con 2 preguntas de lecciones anteriores, elegidas por lo que falló.
- Resultado: `assets/quiz.js` muestra al final una línea para pegar en el chat (`Lección N (...): X de Y. Fallé: P3.`). Con esa línea se actualiza `~/.aprender/registro.md`.
- Ritmo: una lección por vez; la siguiente se arma cuando Tobi pega el resultado.
- Sin hoja de referencia "Revisar a un agente": lo que hay que mirar va en la sección "Qué mirar cuando un agente toca esto" de cada lección.

## Otras notas
- Desde el 2026-11-25 el workspace vive en `docs/aprender/backend/` de la rama `t3/teach-backend-repository`, para que Tobi lo baje en cualquier dispositivo. Commitear y pushear al cerrar cada lección. `CONTEXTO.md` es el traspaso para otro agente.
- `~/.aprender/registro.md` es el registro de repaso espaciado de Tobi (Concepto | Estado | Último repaso | Próximo repaso | Proyecto | Fuente | Nota). Sumar una fila solo con evidencia (el resultado del quiz), no al cubrir el tema.
- Estilo: el de `~/.claude/CLAUDE.md` (español rioplatense simple, conclusión primero, cada término definido la primera vez, "=" para equivalencias).
- El mapa del curso (`reference/mapa.html`) da una respuesta corta a cada pregunta inicial de Tobi. Las lecciones la profundizan.
- 2026-11-25: permisos se adelantó a la lección 4 (antes era la 5) porque Tobi falló dos veces el mismo concepto: creyó que un `if` que compara `CallerId` con el id alcanza. Cuando falla algo dos veces, conviene adelantar la lección que lo enseña en vez de solo repasarlo.
