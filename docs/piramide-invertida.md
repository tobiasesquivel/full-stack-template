# Agent Autonomy — La pirámide invertida de Lauren Tan

## Contexto y filosofía

Lauren Tan (@poteto), desarrolladora de Grok Bot y creadora de pstack, utiliza una metodología de ingeniería orientada a maximizar la autonomía, confiabilidad y eficiencia de los agentes de programación.

Su filosofía parte de una premisa fundamental: **la autonomía de un agente no depende únicamente de la inteligencia del modelo, sino también de la calidad del entorno en el que trabaja.**

En lugar de invertir continuamente tiempo humano y tokens en supervisar, corregir y revisar el trabajo de los agentes, propone invertir ese esfuerzo en mejorar el codebase y sus mecanismos de verificación.

El objetivo es construir un entorno donde hacer lo correcto sea el camino más sencillo y natural, y donde hacer lo incorrecto sea difícil, detectable o directamente imposible.

Los agentes suelen aprender observando el código existente, copiando patrones cercanos y utilizando las abstracciones disponibles. Por eso, la arquitectura y la calidad del propio repositorio son mecanismos fundamentales para dirigir su comportamiento.

Un codebase con malos patrones, múltiples maneras de resolver el mismo problema, abstracciones ambiguas y mecanismos de verificación insuficientes requiere constantemente supervisión humana. Ejecutar más agentes sobre ese entorno puede multiplicar los problemas en lugar de aumentar la productividad.

## La pirámide invertida de confianza

Lauren organiza los mecanismos para mejorar la confiabilidad de los agentes en una pirámide invertida.

Los niveles superiores representan las inversiones de ingeniería que ofrecen mayor impacto acumulativo. Los inferiores dependen cada vez más de la interpretación, el razonamiento y la intervención humana.

La estrategia consiste en dedicar la mayor cantidad de esfuerzo posible a los niveles superiores y reducir progresivamente la dependencia de los inferiores.

### Nivel 1 — Codebase y arquitectura

Es el nivel de mayor impacto.

Antes de decirle a un agente que no cometa determinado error, investigá si es posible modificar el diseño del software para que ese error directamente no pueda ocurrir.

Algunas estrategias:

- Diseñar APIs que sean fáciles de utilizar correctamente y difíciles de utilizar incorrectamente.
- Encapsular detalles de implementación.
- Establecer límites claros entre módulos.
- Definir un único propietario para cada estado y responsabilidad.
- Representar invariantes del dominio mediante el diseño del software.
- Eliminar abstracciones ambiguas.
- Consolidar múltiples maneras de realizar una misma operación.
- Evitar fuentes de verdad duplicadas.
- Eliminar implementaciones obsoletas que los agentes podrían copiar.
- Reducir el conocimiento implícito necesario para modificar el sistema correctamente.
- Diseñar componentes cohesivos con interfaces claras y predecibles.

Por ejemplo, si los agentes acceden repetidamente a las implementaciones internas de otros módulos, no alcanza con agregar una instrucción que les diga que no deben hacerlo. Una solución superior sería establecer límites arquitectónicos y mecanismos de visibilidad que impidan ese acceso.

El codebase debe enseñar mediante su propia estructura.

### Nivel 2 — Controles automatizados y análisis estático

Cuando no sea razonable eliminar un error mediante arquitectura, buscá mecanismos automáticos que permitan detectarlo.

Utilizá, según corresponda:

- Sistemas de tipos.
- Compiladores y sus diagnósticos.
- Linters.
- Analizadores estáticos.
- Architecture tests.
- Análisis de dependencias.
- Restricciones de base de datos.
- Tests automatizados.
- Quality gates de CI.
- Validadores específicos del dominio.

Siempre que sea posible, los errores de estas herramientas deberían ser accionables. No deberían limitarse a indicar que algo está mal, sino orientar al agente hacia la alternativa correcta.

Por ejemplo, en lugar de:

`Invalid dependency detected.`

Es preferible:

`Module A cannot access Module B's internal persistence layer. Use Module B's public application interface instead.`

Esto permite que los agentes detecten, comprendan y corrijan sus propios errores sin necesitar intervención humana.

Cuando se introduce una nueva restricción, también es importante revisar los ejemplos incorrectos existentes. De lo contrario, los agentes pueden seguir copiándolos.

Si existe deuda técnica que no puede eliminarse inmediatamente, considerá impedir nuevas violaciones mientras se corrigen progresivamente las anteriores.

### Nivel 3 — Verificación del comportamiento

Que el código compile y respete la arquitectura no demuestra necesariamente que resuelva correctamente el problema.

Los agentes deben disponer de herramientas para ejecutar, observar, experimentar y verificar el software que construyen.

Según el proyecto, esto puede incluir:

- Tests de integración.
- Tests end-to-end.
- Property-based testing.
- Fuzz testing.
- Ejecución real de la aplicación.
- Inspección de logs, traces y métricas.
- Debugging.
- Screenshots y grabaciones de video.
- Comparaciones del comportamiento antes y después.
- Benchmarks.
- Pruebas de concurrencia.
- Diagramas de estados.
- Model checking.
- Verificación formal.

La evidencia debe ser proporcional a la naturaleza y al riesgo de la modificación.

No es necesario utilizar herramientas de verificación formal en todos los proyectos. Deben seleccionarse cuando aporten suficiente valor.

El objetivo es pasar de afirmaciones como «implementé la funcionalidad y debería funcionar» a demostraciones observables de que el comportamiento esperado realmente se cumple.

También es importante proporcionar herramientas de verificación reutilizables para que cada agente no tenga que descubrir desde cero cómo ejecutar, inspeccionar y probar la aplicación.

### Nivel 4 — Instrucciones, documentación y skills

Las instrucciones escritas son útiles, pero constituyen un mecanismo de cumplimiento menos confiable que los anteriores.

Un agente puede olvidar una instrucción, interpretarla incorrectamente o no encontrarla dentro de su contexto.

Por eso, antes de agregar una nueva regla a AGENTS.md, una skill o cualquier otro documento, evaluá si esa regla puede convertirse en una restricción estructural o una comprobación automatizada.

Reservá las instrucciones escritas principalmente para:

- Decisiones que requieren criterio.
- Preferencias del desarrollador.
- Conocimiento del dominio.
- Convenciones que no pueden verificarse razonablemente.
- Contexto arquitectónico.
- Procesos de trabajo.
- Explicaciones del porqué de determinadas decisiones.

Evitá acumular instrucciones que simplemente describen errores que podrían detectarse automáticamente.

Utilizá progressive disclosure: el agente debe cargar el contexto relevante cuando lo necesite, no recibir todas las instrucciones posibles en cada interacción.

### Nivel 5 — Revisión e intervención humana

La revisión humana es el mecanismo más difícil de escalar.

Si un humano debe detectar y corregir exactamente el mismo problema en cada pull request, existe una oportunidad de mejorar el entorno de ingeniería.

La intervención humana debería concentrarse progresivamente en:

- Decisiones arquitectónicas.
- Requisitos ambiguos.
- Decisiones de producto.
- Trade-offs.
- Seguridad y riesgos importantes.
- Validación de diseños nuevos.
- Problemas que requieren criterio y experiencia.
- Evaluación de incertidumbres.

La finalidad no es eliminar indiscriminadamente la revisión humana, sino evitar que se utilice para tareas repetitivas que podrían resolverse mediante mecanismos superiores de la pirámide.

## El ciclo de mejora continua

Cada corrección realizada a un agente representa una oportunidad para mejorar el sistema.

Cuando detectes un problema, no te limites a pensar cómo corregir esta implementación. Considerá también cómo evitar que futuros agentes cometan la misma categoría de error.

El proceso recomendado es:

1. Identificar el error observado.
2. Investigar si existen antecedentes del mismo problema.
3. Agrupar los errores similares en una categoría común.
4. Identificar su causa raíz.
5. Investigar cómo eliminar esa categoría de errores mediante arquitectura.
6. Si no resulta viable, buscar mecanismos de cumplimiento mediante tipos, análisis estático o CI.
7. Si todavía no puede prevenirse, introducir pruebas que detecten el comportamiento incorrecto.
8. Recurrir a documentación e instrucciones cuando las alternativas superiores no sean razonables.
9. Demostrar que el mecanismo propuesto detectaría o impediría un error real ocurrido anteriormente.
10. Eliminar instrucciones redundantes cuando hayan sido reemplazadas por mecanismos más confiables.

No conviertas automáticamente cada error aislado en una nueva abstracción, restricción o regla.

Priorizá los problemas recurrentes, los errores de alto impacto y aquellos cuya prevención represente un beneficio significativo respecto al coste de implementación y mantenimiento.

## Ejemplos prácticos

### Violaciones arquitectónicas

Problema: los agentes acceden directamente a las implementaciones internas de otros módulos.

Solución inferior: agregar una instrucción prohibiéndolo.

Solución superior: restringir la visibilidad de las implementaciones y utilizar comprobaciones automatizadas de dependencias.

### Estados inválidos del dominio

Problema: los agentes permiten crear entidades con combinaciones inválidas de propiedades.

Solución inferior: documentar todas las combinaciones prohibidas.

Solución superior: diseñar tipos, constructores y operaciones que preserven las invariantes del dominio.

### Errores de autorización

Problema: los agentes olvidan aplicar determinadas restricciones de acceso.

Solución inferior: recordarles que deben comprobar los permisos en cada endpoint.

Solución superior: centralizar los mecanismos de autorización, aplicar configuraciones seguras por defecto y establecer pruebas automatizadas de acceso.

### Uso de patrones obsoletos

Problema: los agentes continúan utilizando una abstracción antigua porque encuentran ejemplos de ella en el repositorio.

Solución inferior: agregar una instrucción que diga que deben utilizar la nueva abstracción.

Solución superior: migrar las implementaciones existentes, eliminar la abstracción anterior o impedir automáticamente nuevos usos.

### Regresiones funcionales

Problema: los agentes corrigen un bug, pero introducen una regresión que vuelve a aparecer posteriormente.

Solución inferior: corregir nuevamente el bug.

Solución superior: incorporar una prueba de regresión que reproduzca el comportamiento incorrecto y falle si vuelve a introducirse.

## Relación entre autonomía y consumo de tokens

Los mecanismos deterministas permiten reutilizar decisiones de ingeniería sin tener que consumir tokens razonando nuevamente sobre ellas.

Un linter, una restricción arquitectónica o un sistema de tipos puede realizar miles de comprobaciones sin necesidad de que un modelo inspeccione manualmente cada caso.

Esto permite:

- Reducir el número de correcciones humanas.
- Reducir las rondas de code review.
- Reducir el consumo de tokens en tareas repetitivas.
- Facilitar el trabajo paralelo de múltiples agentes.
- Aumentar la consistencia entre implementaciones.
- Mejorar la capacidad de los agentes para corregir su propio trabajo.
- Reducir el conocimiento previo necesario para contribuir correctamente al repositorio.

Antes de incrementar la cantidad de agentes trabajando en paralelo, es preferible aumentar la confiabilidad del entorno en el que trabaja cada agente individualmente.

La autonomía debe ser una consecuencia de la confiabilidad del sistema, no un sustituto de ella.

## Cómo aplicar esta metodología en mis proyectos

Cuando te solicite aplicar esta filosofía, analizá primero el contexto real del repositorio.

Investigá:

- La arquitectura existente.
- Los patrones de implementación.
- Los límites entre componentes.
- Las herramientas de análisis estático.
- Las pruebas automatizadas.
- Los mecanismos de CI.
- Los comentarios de code reviews.
- El historial de commits y reverts.
- Las correcciones recurrentes.
- Las instrucciones que actualmente dependen exclusivamente del cumplimiento voluntario de los agentes.

Buscá oportunidades de trasladar responsabilidades desde los niveles inferiores hacia los superiores de la pirámide.

Para cada oportunidad identificada, explicá:

- Qué categoría de error estamos intentando prevenir.
- Qué evidencia demuestra que existe el problema.
- Cuál es su causa raíz.
- Qué mecanismo proponés.
- En qué nivel de la pirámide se encuentra.
- Por qué no resulta conveniente resolverlo en un nivel superior, si corresponde.
- Cuál sería el impacto esperado.
- Qué coste y complejidad de mantenimiento introduciría.
- Cómo verificaríamos que realmente funciona.

Priorizá soluciones simples, reutilizables y compatibles con la arquitectura existente.

No introduzcas complejidad arquitectónica únicamente para imponer restricciones. El objetivo es mejorar el software y su mantenibilidad, no construir una colección innecesaria de mecanismos de control.

Si está disponible, utilizá la skill `correct` de pstack como herramienta especializada para investigar errores recurrentes y proponer mejoras estructurales.

## Mis restricciones personales

Esta metodología está inspirada en Lauren Tan, pero no implica adoptar todas sus preferencias de autonomía.

En particular:

- No autorizo el merge autónomo de pull requests. (En full-stack-template lo reemplaza la regla 6 de `AGENTS.md`: Tobi, 2026-11-27, "hace todo autonomo incluido merges".)
- Las mejoras arquitectónicas y los nuevos mecanismos preventivos propuestos mediante esta metodología requieren mi aprobación antes de implementarse.
- No amplíes automáticamente el scope de una tarea para realizar mejoras no solicitadas.
- No conviertas AGENTS.md en una acumulación de reglas, correcciones y guardrails.
- Conservá la documentación especializada en archivos independientes, consultados bajo demanda.
- Evitá ejecutar múltiples agentes o verificaciones redundantes cuando existan mecanismos más económicos y confiables.

Este documento es material de contexto. No constituye por sí mismo una autorización para modificar el repositorio ni ejecutar un workflow determinado.

## Principio fundamental

Cada vez que un humano tenga que corregir repetidamente a un agente, existe una oportunidad de trasladar esa corrección a un mecanismo más confiable.

No busques únicamente mejorar la respuesta del agente actual. Buscá mejorar el entorno para que todos los agentes futuros puedan trabajar correctamente con menos supervisión.

El objetivo final es construir un codebase que guíe, restrinja, verifique y facilite el trabajo de los agentes mediante su propio diseño.

## Fuentes originales

- Lauren Tan — All Code Is Slop (Except Mine): https://www.youtube.com/watch?v=O_zcc6Xl12g
- Pstack: https://github.com/cursor/plugins/tree/main/pstack
- Correct: https://github.com/cursor/plugins/tree/main/pstack/skills/correct
- Make it yours — Pstack guide: https://github.com/cursor/plugins/blob/main/pstack/docs/guide/09-make-it-yours.md

Este documento sintetiza y adapta la metodología original a mis preferencias personales de desarrollo.
