# Users

## Purpose

Contrato de perfiles bajo /api/v1/users. Los requisitos no afirman ejecución verificada.

## Requirements

### Requirement: Perfil separado

UserProfile MUST contener AuthId, DisplayName y Timezone, sin duplicar el nombre de login. Identity MUST conservar userName y email canónicos. UserResponse MUST contener {authId,userName,email,displayName,timezone,roles,permissions}. GET /me MUST devolver 200 con el perfil del llamador. PUT /me MUST aceptar {displayName,timezone}, validar una zona IANA y devolver el perfil actualizado. Peticiones sin JWT válido MUST devolver 401.

#### Scenario: Edición propia
- GIVEN un usuario autenticado.
- WHEN cambia nombre visible y timezone IANA válidos.
- THEN PUT /api/v1/users/me MUST devolver 200 y GET /me MUST reflejar ambos valores.
- AND el nombre de login MUST permanecer igual.

#### Scenario: Zona inválida
- GIVEN un usuario autenticado.
- WHEN envía una zona no válida.
- THEN MUST recibir 400 ValidationProblem sin guardar el perfil.

### Requirement: Owner-or-permission

GET /{authId} MUST autorizar propietario o Permissions.Users.Read. DELETE /{authId} MUST autorizar propietario o Permissions.Users.Manage. El módulo MUST cargar primero por AuthId, devolver 404 si falta y después 403 si el recurso existe pero no hay acceso. DELETE /me MUST permitir el borrado propio. User MUST NOT obtener acceso global a perfiles ajenos. SuperAdmin MUST tener Users.Manage.

#### Scenario: Perfil ajeno
- GIVEN un usuario ordinario y un perfil de otro usuario.
- WHEN consulta o borra ese perfil.
- THEN MUST recibir 403, no una respuesta pública con datos personales.

#### Scenario: Perfil ausente
- GIVEN un usuario autenticado.
- WHEN consulta un AuthId inexistente.
- THEN MUST recibir 404.

#### Scenario: Permiso elevado
- GIVEN un llamador con el permiso requerido para un perfil ajeno.
- WHEN ejecuta la operación correspondiente.
- THEN MUST poder acceder sin ser propietario.

### Requirement: Cascadas y migración

Borrar identidad MUST cascadear perfil, tareas y sesiones y devolver 204. TaskItem.OwnerId MUST referenciar UserProfile.AuthId. Migraciones MUST conservar las históricas, renombrar Users a UserProfiles, hacer backfill de DisplayName y UTC, y conservar tareas. MUST NOT recrear la base ni usar EnsureCreated.

#### Scenario: Borrado completo
- GIVEN un usuario con perfil, tareas y varias sesiones.
- WHEN un llamador autorizado borra su identidad.
- THEN MUST desaparecer esos datos y ninguna tarea de otro propietario.
