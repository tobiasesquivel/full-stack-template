# Tasks

## Purpose

Contrato de tareas personales bajo /api/v1/tasks. Los requisitos no son resultados de tests.

## Requirements

### Requirement: Estado final y propiedad

Todas las rutas MUST exigir JWT válido. POST / MUST aceptar {title}, derivar OwnerId del llamador y devolver 201 con {id,title,isCompleted,createdAt,updatedAt}. La tarea nueva MUST empezar incompleta. PUT /{id} MUST aceptar {title,isCompleted} y devolver 200 con estado final. GET /{id} MUST devolver el mismo contrato. DELETE /{id} MUST devolver 204. Title MUST recortarse y tener entre 1 y 200 caracteres. OwnerId MUST NOT aceptarse del cliente. Fechas MUST representar UTC. El endpoint toggle MUST NOT existir.

#### Scenario: Crear y actualizar
- GIVEN un llamador autenticado con Permissions.Tasks.Create.
- WHEN crea una tarea y repite el mismo PUT válido.
- THEN MUST conservar título y completitud finales sin invertir el estado.

#### Scenario: Título inválido
- GIVEN un título vacío tras trim o mayor de 200 caracteres.
- WHEN crea o modifica una tarea.
- THEN MUST recibir 400 ValidationProblem sin guardar cambios.

### Requirement: Lista personal paginada

GET /?page=1&pageSize=20 MUST devolver {items,totalCount,page,pageSize}. Page MUST ser al menos 1 y pageSize MUST estar entre 1 y 100. La lista MUST filtrar solo al propietario actual, incluso si tiene permisos globales. El conteo MUST usar el mismo filtro.

#### Scenario: Aislamiento y paginación
- GIVEN dos propietarios con tareas y un llamador con permiso global.
- WHEN el llamador consulta una página personal válida.
- THEN MUST recibir solo sus tareas y el total de sus tareas.

#### Scenario: Página inválida
- GIVEN page 0 o pageSize fuera de rango.
- WHEN consulta la lista.
- THEN MUST recibir 400 sin sustituir silenciosamente el valor.

### Requirement: Existencia antes de autorización

El módulo MUST cargar una tarea individual solo por Id, devolver 404 si falta y después autorizar propietario o permiso de acción. MUST NOT combinar Id y OwnerId para la carga individual. User MUST tener solo Tasks.Create como permiso global. Admin y SuperAdmin MUST tener permisos globales Tasks.Read, Tasks.Update y Tasks.Delete además de creación.

#### Scenario: Recurso ajeno
- GIVEN una tarea existente de otro propietario y un usuario ordinario.
- WHEN intenta leer, actualizar o borrar por Id.
- THEN MUST recibir 403 y la tarea MUST permanecer intacta.

#### Scenario: Recurso ausente
- GIVEN un Id de tarea inexistente.
- WHEN un usuario autenticado consulta o modifica ese Id.
- THEN MUST recibir 404.

#### Scenario: Administrador sobre tarea ajena
- GIVEN un administrador con el permiso de acción.
- WHEN opera una tarea ajena por Id.
- THEN MUST poder completar la operación.
