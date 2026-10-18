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
Un acceso autenticado que vive en el servidor y se abre, se usa y se cierra de forma independiente de otros accesos del mismo usuario. Vence por tiempo absoluto o por inactividad. Revocar una sesión es cerrarla desde el servidor; no cierra las otras sesiones del usuario.
_Avoid_: identidad, cuenta, token.
