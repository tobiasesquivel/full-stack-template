# Registro de repaso del curso del backend

Una fila por concepto, con evidencia del quiz. Se usa cuando `~/.aprender/registro.md` no existe en la máquina.

| Concepto | Estado | Último repaso | Próximo repaso | Proyecto | Fuente | Nota |
|---|---|---|---|---|---|---|
| Recorrido de un pedido: endpoint → bus → handler → base | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0001-el-recorrido-de-un-pedido.html | Lección 1: 5 de 5. |
| Mediator con Wolverine (`IMessageBus.InvokeAsync`) | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0001-el-recorrido-de-un-pedido.html | Lección 1: 5 de 5. Acertó P2 (agente que saltea el bus). Repaso en la lección 2 (R-L1-P2). |
| `ErrorOr` + `ApiResults` → status HTTP | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0001-el-recorrido-de-un-pedido.html | Lección 1: 5 de 5. Acertó P4 (null pierde 403 frente a 404). Repaso en la lección 2 (R-L1-P4). |
| Parameter binding en minimal APIs | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0001-el-recorrido-de-un-pedido.html | Lección 1: 5 de 5. Acertó P3 (el bus sale del contenedor). |
| Archivos de una slice (query, handler, endpoint, tests) | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0002-los-archivos-de-una-slice.html | Lección 2: 6 de 7. Acertó P1. |
| Forma del handler: `Handler` anidado en el record `partial`, en `<Mensaje>Handler.cs` | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0002-los-archivos-de-una-slice.html | Lección 2: acertó P2. Repaso en la lección 3 (R-L2-P2). |
| `Contracts/` solo para lo que usa otra feature | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0002-los-archivos-de-una-slice.html | Lección 2: acertó P4. |
| `CallerId` viaja en la query porque el handler no ve HTTP | sabido | 2026-11-25 | 2026-11-28 | full-stack-template | lessons/0002-los-archivos-de-una-slice.html | Lección 2: acertó P3. |
| El permiso lo decide el handler con `CanAccessAsync` (dueño o permiso), no el endpoint | fallado | 2026-11-25 | 2026-11-26 | full-stack-template | lessons/0002-los-archivos-de-una-slice.html | Lección 2: falló P5 (un `if` en el endpoint deja afuera al Admin). Repaso en la lección 3 (R-L2-P5) y otra vez en la lección 5. |
