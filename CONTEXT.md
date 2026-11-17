# TemplateApp

Vocabulario de perfiles, tareas y acceso a recursos personales.

## Language

**Perfil de usuario (UserProfile)**:
Los datos personales que acompañan a una identidad, incluidos el nombre visible y la zona horaria.
_Avoid_: cuenta, identidad, usuario como sinónimo de perfil.

**Datos personales**:
Lo que dice algo de una persona: el nombre de login, el email, las credenciales, el nombre visible, la zona horaria y todo texto libre que escribe el usuario, como el título de una tarea. Un id opaco (el `AuthId`, un GUID al azar) no se considera dato personal en la auditoría: sin la tabla de Identity no dice quién es.
_Avoid_: PII como sinónimo exacto; para el GDPR el id seudónimo sigue siendo dato personal.

**Auditoría (AuditEntry)**:
El registro de quién cambió qué entidad y cuándo, sin datos personales.
_Avoid_: log, historial de valores, event sourcing.

**Propietario (owner)**:
El usuario al que pertenece una tarea o un perfil.
_Avoid_: autor, asignado, usuario dueño.

**Tarea (TaskItem)**:
Una actividad personal con título y estado de completitud que pertenece a un propietario.
_Avoid_: ticket, trabajo, recurso como sinónimo de tarea.

**Permiso**:
Una capacidad nombrada para una acción sobre una clase de recursos. Puede permitir actuar sobre recursos ajenos.
_Avoid_: rol, permiso propio, permiso global como concepto distinto.

**Rol**:
Una agrupación de permisos asignada a un usuario. User, Admin y SuperAdmin son los roles del template.
_Avoid_: permiso, categoría de perfil.

**Sesión**:
Un acceso autenticado que vive en el servidor y se abre, se usa y se cierra de forma independiente de otros accesos del mismo usuario. Vence por tiempo absoluto o por inactividad. Revocar una sesión es cerrarla desde el servidor; no cierra las otras sesiones del usuario.
_Avoid_: identidad, cuenta, token.
