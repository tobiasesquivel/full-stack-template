# Auth

## Purpose

Contrato de identidad y sesiones bajo /api/v1/auth. Estos requisitos son normativos, no evidencia de ejecución.

## Requirements

### Requirement: Registro y login protegidos

GET /csrf MUST emitir XSRF-TOKEN legible y la cookie interna antiforgery. POST /register, /login, /refresh y /logout MUST validar X-XSRF-TOKEN incluso para visitantes anónimos. La cookie de refresh MUST ser HttpOnly, restringida a /api/v1/auth, SameSite conservador y Secure fuera de Development. El refresh token MUST NOT aparecer en JSON.

POST /register MUST aceptar {userName,email,password,displayName} y devolver 201 con {user,accessToken,accessTokenExpiresAt}. POST /login MUST aceptar {userName,password} y devolver 200 con el mismo contrato. user MUST seguir el contrato Users. El perfil inicial MUST tener timezone UTC. El registro MUST confirmar identidad, perfil, rol User y sesión en una transacción explícita. Las restricciones únicas MUST resolver carreras de email y nombre.

#### Scenario: Registro válido
- GIVEN un visitante con antiforgery válido y datos válidos no registrados.
- WHEN envía POST /api/v1/auth/register.
- THEN recibe 201, la cookie de sesión y un access token con vencimiento.
- AND identidad, perfil, rol y sesión existen juntos en PostgreSQL.

#### Scenario: Duplicado concurrente
- GIVEN dos registros concurrentes con el mismo email o nombre.
- WHEN la base aplica las restricciones únicas.
- THEN solo uno MUST confirmar y el otro MUST recibir 409 sin filas parciales.

#### Scenario: Login incorrecto
- GIVEN un nombre inexistente o una contraseña incorrecta.
- WHEN envía login con antiforgery válido.
- THEN recibe el mismo 401 sin revelar cuál credencial falló.

#### Scenario: Perfil ausente
- GIVEN credenciales válidas pero ningún perfil para esa identidad.
- WHEN inicia sesión.
- THEN MUST fallar como error interno Auth.ProfileMissing, no 404 ni identidad parcial.

#### Scenario: Login CSRF
- GIVEN una petición sin token antiforgery válido.
- WHEN intenta register, login, refresh o logout.
- THEN MUST recibir 400 y MUST NOT modificar sesión ni identidad.

### Requirement: Rotación por familia

POST /refresh MUST usar únicamente la cookie, sin body, y devolver 200 con {accessToken,accessTokenExpiresAt} y cookie nueva. Tokens opacos MUST guardarse como hash con FamilyId y vencimiento. La redención MUST ser atómica. Reutilizar un token rotado MUST revocar la familia persistida y devolver 401. Ninguna heurística MUST distinguir replay benigno de malicioso. POST /logout MUST revocar la sesión actual, borrar la cookie y devolver 204 idempotente sin exigir access token vigente.

#### Scenario: Dos redenciones
- GIVEN dos peticiones con el mismo token vigente.
- WHEN compiten por rotarlo.
- THEN solo una MUST rotar y el reuso MUST devolver 401 y revocar su familia.
- AND otra sesión independiente del mismo usuario MUST permanecer vigente.

#### Scenario: Sesión inexistente o vencida
- GIVEN ninguna cookie válida o un token vencido.
- WHEN pide refresh con antiforgery válido.
- THEN recibe 401 sin nueva sesión.

#### Scenario: Logout repetido
- GIVEN una sesión cerrada y antiforgery válido.
- WHEN repite logout.
- THEN recibe 204.

### Requirement: Cliente y permisos vigentes

JWT MUST contener identidad y vigencia, nunca roles ni permisos. Permisos MUST consultarse scoped por request, sin cache global. Angular MUST guardar access token solo en memoria, restaurar con refresh al recargar y coordinar una sola renovación pendiente por pestaña. MUST NOT usar localStorage ni sessionStorage para credenciales o tokens.

#### Scenario: Cambio de permiso
- GIVEN un access token aún vigente y una modificación de permisos en la base.
- WHEN se realiza la siguiente petición autorizada.
- THEN la decisión MUST reflejar el estado nuevo sin reemitir JWT.

#### Scenario: Recarga del cliente
- GIVEN la cookie de sesión vigente.
- WHEN el navegador recarga.
- THEN Angular MUST obtener antiforgery y restaurar con refresh antes de cargar datos personales.
