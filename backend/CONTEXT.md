# TemplateApp — Backend

Contexto del backend .NET del template: autenticación, autorización de recursos y las
convenciones que la golden slice demuestra en código.

## Language

**Propietario (owner)**:
El usuario que posee una instancia de recurso. Toda entidad sujeta a autorización de recursos
expone su dueño como `OwnerId`.
_Avoid_: AuthId (para propiedad), "usuario dueño"

**Recurso**:
Una instancia de entidad que un usuario puede tocar y cuya autorización depende de quién la posee.
_Avoid_: entidad, item, "tarea" genérico

**Existencia vs. Propiedad**:
Dos chequeos estructuralmente separados: ¿el recurso existe? (404) y ¿el llamador es dueño o
tiene permiso? (403). Nunca se combinan en un solo filtro.
_Avoid_: "no encontrado o no tuyo", colapsar ambos en el `WHERE`

**Permiso**:
Capacidad nombrada `Permissions.{Recurso}.{Acción}`, sin jerarquías ni comodines. Tener el
permiso habilita operar cualquier recurso ("Any"); ser dueño habilita operar el propio ("Own") —
el patrón owner-or-permission es el scope.
_Avoid_: scope, `Tasks.*`, "claim de permiso"

**Rol**:
Agrupación estable de permisos asignada a usuarios (SuperAdmin, Admin, User). Los seeders
definen el estado inicial; la base de datos es la fuente de verdad en runtime.
_Avoid_: "permisos del rol" como sinónimo de rol

**Autorización de recursos**:
El chequeo owner-or-permission que decide si un usuario puede operar un recurso dado. Vive en un
handler genérico único, no por entidad.

**Perfil de usuario (UserProfile)**:
Los datos del usuario que no son de identidad — nombre visible y zona horaria. Es el único punto
del dominio que referencia la identidad: su `AuthId` es el id de identity, y los recursos le
apuntan como dueño.
_Avoid_: "usuario" para la fila de Identity, duplicar el nombre de login
