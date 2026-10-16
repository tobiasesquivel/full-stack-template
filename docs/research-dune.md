# Dune de Lauren Tan: investigación pública

Consulta: 2026-10-05. Esta nota describe el framework de Lauren (@poteto), no el sistema local de reglas Dune/TemplateApp. No se cambió código de la aplicación.

## Uso en próximas sesiones

Consultar esta nota antes de explicar Dune, comparar su arquitectura con este template o diseñar cambios inspirados en él. Las fuentes externas describen el framework de Lauren; las reglas locales vigentes están en `openspec/specs/dune/spec.md`.

Conservar la distinción entre declaraciones de la autora, transcripción de terceros e inferencias. Los ejemplos de chat de la conversación eran ilustrativos, no APIs reales. Para actualizar un dato, citar la nueva fuente y su fecha. Verificar especialmente disponibilidad pública y APIs: pueden cambiar después de esta consulta.

## Conclusión

Dune es el framework interno de cliente que Lauren creó para Grok Bot. Su objetivo declarado es que agentes con poco contexto produzcan cambios correctos por defecto. No es solo lint ni una colección de skills. En el anuncio original, Dune todavía era un nombre provisional. [S1][S2]

La idea central es que el camino más fácil también sea el camino correcto. Lauren describe convenciones de ubicación, límites de importación, protección del renderer y limpieza de patrones que los agentes podrían copiar. Son declaraciones de la autora, no una auditoría independiente de la implementación. [S1][S3]

## Fuentes y nivel de confianza

- **S1 — Fuente primaria:** [anuncio de Grok Bot, 11 de agosto](https://x.com/poteto/status/2087227636590465030). Lauren presenta Dune como framework de cliente diseñado para agentes con muy poco contexto. En una respuesta dice que sus agentes fusionan sus propios PRs.
- **S2 — Fuente primaria:** [The Complete Guide to pstack Pt. 2](https://x.com/poteto/status/2097732320606507506). Lo define como «our in-house client framework for desktop apps». Describe diseño empezando por un tutorial y documentación para sus usuarios.
- **S3 — Voz de la autora en republicación:** [charla original en X](https://x.com/poteto/status/2102050467505430555), [copia en YouTube](https://www.youtube.com/watch?v=NjoZoUm85x0). La descripción de la copia enlaza al original. Se obtuvo la transcripción automática completa; sus nombres propios contienen errores, por lo que no se trata como una referencia de APIs. Las diapositivas originales se revisaron previamente entre los minutos 27 y 30.
- **S4 — Transcripción de tercero:** [gist de michaelaflores](https://gist.github.com/michaelaflores/5bfed43ea91f89f425be0915c025d2d6), [metadatos y contenido mediante GitHub](https://api.github.com/gists/5bfed43ea91f89f425be0915c025d2d6). El usuario lo identifica como texto de una captura de Lauren. El gist no incluye la captura ni un enlace que permita comprobar esa procedencia de forma independiente. El contenido tiene frases cortadas al inicio, aunque la API marca el archivo como no truncado.
- **S5 — Fuente primaria sobre verificación:** [The Complete Guide to pstack Pt. 1](https://x.com/poteto/status/2094457600259842065). Describe CLI, mapa de funcionalidades, mantenimiento y ejecución de la aplicación real.
- **S6 — Fuente primaria sobre React y disponibilidad:** [publicación de Lauren, 17 de agosto](https://x.com/poteto/status/2089227731305464150). Define Dune como framework interno de React para escritorio. Dice que prohíbe `useEffect` directo y lo expone mediante hooks del framework. Ante una pregunta sobre publicar Dune como código abierto responde «none yet»; esa respuesta corresponde a esa fecha, no a una garantía sobre el futuro.
- **S7 — Contexto de otro autor:** [publicación de Mark Villacampa, 12 de agosto](https://x.com/MarkVillacampa/status/2087458215386632229). Identifica Sand como nombre interno del producto. Es contexto adicional, no documentación técnica del framework.

## Qué añade el gist

El texto presenta cinco reglas. Su redacción se puede comprobar en el gist; su atribución exacta a una página original de Dune no está comprobada. Las reglas encajan con la explicación pública de Lauren sobre convenciones, límites y agentes con contexto reducido. [S3][S4]

| Regla del gist | Lectura práctica, no API de Dune |
| --- | --- |
| El camino convencional requiere menos decisiones que un atajo. | Ofrecer una operación clara para cada trabajo, sin obligar al componente a resolver transporte o almacenamiento. |
| Las dependencias prohibidas fallan mecánicamente. | Comprobar límites con herramientas, no solo pedir que se respeten. |
| Cada valor persistente tiene un escritor obvio. | Definir qué módulo tiene autoridad para cambiar un dato. |
| El trabajo nuevo añade archivos aislados, no ramas en raíces compartidas. | Extender una funcionalidad sin editar inventarios centrales para cada incorporación. |
| Las excepciones son pequeñas, explícitas y revisadas como cambios de arquitectura. | No convertir una necesidad especial en permiso general para saltarse límites. |

### Los cinco conceptos

- **Feature:** carpeta con dueño claro para una funcionalidad. Lauren confirma que sus piezas se colocan juntas. [S3][S4]
- **Entrypoint:** punto de entrada de una vista en React, parecido a una ruta. El gist añade que se descubre mediante archivos reservados. No publica sus nombres exactos ni el mecanismo de descubrimiento. [S3][S4]
- **Transcript card:** representación de una entrada del transcript de Grok Bot, propiedad de una funcionalidad. El gist también indica descubrimiento mediante archivos reservados. No es una tarjeta genérica de cualquier interfaz. [S3][S4]
- **Client:** el gist le asigna el estado persistente del equipo local, con un escritor detrás de hooks y comandos con nombre. La diapositiva de la charla lo describe como estado detrás de hooks y comandos. La política concreta de persistencia o sincronización no aparece en estas fuentes. [S3][S4]
- **Host:** comportamiento detrás de un contrato tipado. En la explicación oral, Lauren sitúa un host en la máquina virtual de Grok Bot. La diapositiva separa extensiones del host del proceso principal de Electron; no deben confundirse. [S3][S4]

### Descubrimiento sin inventarios compartidos

El gist dice: «Its Entrypoints and Transcript cards are discovered from reserved files rather than registered in shared inventories». [S4]

[INFERENCE] Esto reduce la necesidad de que dos agentes editen una lista central para incorporar dos funcionalidades distintas. No demuestra que desaparezcan todos los conflictos de integración. No se conoce si Dune usa generación de código, herramientas de build o descubrimiento en runtime.

### Framework separado de la aplicación

El gist ubica Dune en `sand/dune` y la aplicación en `sand/src`; afirma que Dune nunca importa código de la aplicación. [S4]

[INFERENCE] La separación busca que la aplicación consuma el framework sin introducir dependencias inversas. Mark Villacampa identifica Sand como nombre interno del producto, pero estas fuentes no describen su estructura completa de paquetes. [S7]

## Hallazgos adicionales de fuentes primarias

### React, hooks y efectos controlados

Lauren define Dune como un framework interno de React para aplicaciones de escritorio. Explica que prohíbe usar `useEffect` directamente en el producto y lo expone de forma indirecta mediante hooks proporcionados por el framework. No significa que React deje de tener efectos, sino que el framework controla su uso. No publica los nombres de esos hooks ni el código de su implementación. [S6]

En la misma publicación describe migraciones a React y StyleX por rendimiento y mantenimiento. Es el contexto del proyecto; no demuestra que cada aplicación Dune deba usar StyleX ni que React Compiler sea una dependencia obligatoria de Dune. [S6]


### Origen: rendimiento y experiencia con Cursor

Lauren explica que Dune incorpora lecciones de problemas de rendimiento del agent window de Cursor. Una de ellas fue impedir que código de otros procesos termine ejecutándose en el renderer de Electron. [S3]

### Restricciones mediante el grafo de dependencias

En la charla dice que Dune contiene código que impone límites mediante el grafo de importaciones y dependencias. La finalidad concreta que explica es evitar trabajo lento en el renderer. No publica el algoritmo ni la configuración del verificador. [S3]

Esto no es una garantía absoluta de rendimiento: evitar importaciones indebidas elimina una clase de errores, pero no prueba que todo el trabajo permitido cumpla el presupuesto de un frame. Esta limitación es una interpretación técnica, no una promesa de la autora.

### El código como memoria

Lauren observa que los agentes tienden a extender los patrones que ven. Un workaround repetido puede convertirse en el patrón normal del proyecto. Su propuesta es borrar deuda, conservar un camino convencional y añadir restricciones contra patrones dañinos. [S3]

### Prohibición de comentarios

Lauren dice que eligieron prohibir comentarios en Dune porque los agentes los usaban como justificación para conservar soluciones parciales y copiarlas. No se publicó en las fuentes consultadas una política completa de excepciones, documentación de APIs o alcance del bloqueo. Es una decisión de ese proyecto, no una regla universal demostrada para todo software. [S3]

### No equivale a verificación formal

La misma charla diferencia verificar una aplicación con herramientas reales de demostrar propiedades mediante métodos formales. Presenta esta segunda opción como difícil y todavía abierta. Dune y sus restricciones no deben describirse como prueba formal de toda la lógica de negocio. [S3]

## Dune, pstack y verificación

- **Dune** organiza y restringe el código de la aplicación. [S1][S2][S3]
- **pstack** organiza cómo trabajan los agentes: investigación, diseño, prototipos, implementación y verificación. [S2][S5]
- **La skill de verificación** enseña a controlar la aplicación real con una CLI y un mapa de funcionalidades. [S5]

Que Lauren use los tres juntos no significa que instalar pstack instale Dune, ni que una regla de lint sustituya la verificación del comportamiento.

## Preguntas todavía abiertas

### Disponibilidad de código

La evidencia más concreta es la respuesta de Lauren del 17 de agosto: todavía no había planes de abrir Dune como código público. No se encontró una implementación oficial descargable en las fuentes públicas consultadas. Esa búsqueda no prueba que no exista un repositorio con otro nombre o una publicación posterior. [S6]

La investigación adicional revisó búsquedas de `Dune` y `Sand` asociadas a poteto, referencias al path `sand/dune`, los cinco conceptos y disponibilidad open source. El investigador consultó también los repositorios públicos de poteto y una búsqueda de repositorios `dune user:poteto`. No encontró una implementación oficial. Las búsquedas generales producen numerosos homónimos no relacionados: herramientas de OCaml, análisis de blockchain y otros proyectos llamados Dune.


Las fuentes leídas no contienen la API pública completa, un tutorial íntegro de Dune, los nombres de los archivos reservados, el almacenamiento del Client, el protocolo del Host ni el código del comprobador de dependencias. No se puede reconstruir una implementación fiel a partir de este extracto y la charla. [S2][S3][S4]

No se comprobó de forma independiente el volumen de PRs, su calidad o la seguridad de fusionarlos automáticamente. Son cifras y prácticas declaradas por Lauren. [S1][S3]
