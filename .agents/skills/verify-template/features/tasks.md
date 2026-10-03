# Tasks

El usuario crea, lista, renombra, completa y elimina sus tareas. La segunda lectura confirma persistencia. Otro usuario no puede operar una tarea ajena sin permiso global.

## Sub-features

- `tasks-create` guarda un título recortado.
- `tasks-list` presenta Tasks propias con paginación.
- `tasks-edit` guarda título y estado final.
- `tasks-delete` elimina después de confirmación.
- `tasks-isolation` distingue recurso ajeno 403 de recurso ausente 404.

## How to get to it (user POV)

- Después de registrarte o entrar, abre `/tasks`.
- Pulsa el enlace `Mis tareas` de la navegación principal.
- Un cliente HTTP usa `POST` y `GET /api/v1/tasks`, y `GET`, `PUT` y `DELETE /api/v1/tasks/{id}`.

## Driving it with browser and verify-template

Preconditions:

- `doctor` pasó y el navegador tiene una sesión creada mediante la UI real.
- Usa el título `Prueba de runtime` en la base aislada de esta ejecución.

Rellena `#new-task-title` con `Prueba de runtime`. Pulsa el botón `Crear` del formulario `Crear tarea`. Exige `POST /api/v1/tasks` 201 y una fila visible con ese título. Captura el estado antes y después.

En la fila que contiene el título, pulsa `Editar`. Rellena `input[aria-label="Editar título"]` con `Prueba persistida`. Pulsa `Guardar` dentro de esa fila. Exige `PUT /api/v1/tasks/{id}` 200. Marca el checkbox con nombre accesible `Marcar hecha: Prueba persistida`. Exige otra respuesta PUT 200 con `isCompleted=true`.

Recarga la página. Espera el refresh de sesión y la lista. Exige el título `Prueba persistida` y el checkbox marcado. La recarga es la segunda lectura que prueba el dato persistido, no una modificación directa del estado Angular.

Pulsa `Eliminar` dentro de la misma fila. Acepta la confirmación `¿Eliminar «Prueba persistida»?`. Exige DELETE 204. Recarga y exige que esa fila no aparezca.

Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs api --run-id repro-template`. El smoke crea dos usuarios reales, comprueba páginas sin IDs repetidos, lectura propia, PUT idempotente, 403 de un usuario ajeno y 404 para una tarea inexistente. No inyecta permisos ficticios por headers.

Para la paginación de la UI, crea once títulos distintos en esta misma instancia. Exige `Siguiente` habilitado, pulsa el botón y observa una página distinta. Usa el nav `Paginación`, no coordenadas. Borra esos datos mediante la UI o elimina la base aislada en cleanup.

## Gotchas

- El checkbox envía estado final por PUT. No existe endpoint toggle.
- Los botones `Editar`, `Guardar` y `Eliminar` se repiten por fila. Selecciona la fila por su título antes de actuar.
- Un 200 de la lista sin el título creado no prueba la creación.
- Un error de conexión debe verse como fallo del recorrido, no como lista vacía verificada.
- Si la API no se levantó, Angular servido por sí solo no verifica Tasks.
