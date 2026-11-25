# Corrigió la idea de que comparar ids alcanza para decidir un permiso

Tobi creía que un `if (CallerId != authId)` resolvía el permiso: falló esa pregunta en las lecciones 2 y 3. Además no sabía qué era `CallerId` (preguntó "¿qué es el caller?" el 2026-11-25). Después de la lección 4 acertó las dos preguntas que atacaban el error: la ruta de un cierre no trae al dueño, y el mismo `if` en el handler sigue salteando "¿tiene el permiso?".

**Evidence**: lección 4, 5 de 7, con P2 y P5 correctas (2026-11-25).

**Implications**: todavía confunde qué permiso da cada rol (falló P4: `Read` frente a `Manage`). Cada término nuevo se define con una frase la primera vez y se repite junto al nombre (`CallerId` (quién pide)) hasta que Tobi lo use solo: una definición de una línea no alcanzó.
