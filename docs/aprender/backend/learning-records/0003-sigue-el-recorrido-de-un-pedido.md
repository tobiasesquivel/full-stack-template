# Sigue el recorrido de un pedido y detecta un agente que saltea el bus

Tobi sigue un `GET` desde el endpoint hasta el handler pasando por Wolverine, y sabe que `ApiResults` traduce el `ErrorOr` a HTTP. Detectó los dos errores de agente de la lección 1: crear el handler a mano y devolver `null` en vez de `ErrorOr`.

**Evidence**: quiz de la lección 1, 5 de 5 (2026-11-25).

**Implications**: la lección 2 no vuelve a explicar el mediator; lo da por sabido y pasa a los archivos de la slice. Los repasos usan variantes más sutiles del mismo error (pedir el handler como parámetro, devolver el error equivocado).
