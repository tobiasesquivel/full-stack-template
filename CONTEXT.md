# TemplateApp

Vocabulario de perfiles, tareas y acceso a recursos personales.

## Language

**Perfil de usuario (UserProfile)**:
Los datos personales que acompañan a una identidad, incluidos el nombre visible y la zona horaria.
_Avoid_: cuenta, identidad, usuario como sinónimo de perfil.

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
Un acceso autenticado que puede renovarse y cerrarse de forma independiente de otros accesos del mismo usuario.
_Avoid_: identidad, cuenta.

**Familia de sesión**:
El conjunto de renovaciones sucesivas de una misma sesión. Revocar una familia no equivale a revocar todas las sesiones del usuario.
_Avoid_: todas las sesiones, familia de usuario.
