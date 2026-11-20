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
El usuario al que pertenece una tarea, un proyecto o un perfil.
_Avoid_: autor, asignado, usuario dueño.

**Propietario borrado (lápida)**:
Un propietario cuya cuenta se borró, anotado en Tasks cuando llega el evento. Ninguna tarea nueva puede quedar a su nombre: crearla responde 401. La anotación no se borra.
_Avoid_: tombstone, soft delete, usuario inactivo.

**Cierre de cuenta**:
El proceso que borra una cuenta en pasos: Users la marca en cierre, Tasks retiene sus tareas y Auth decide si la borra. Si Auth la rechaza, los pasos anteriores se deshacen y la cuenta vuelve a estar activa con el motivo del rechazo. Lo identifica un id de cierre que el usuario recibe al pedirlo.
_Avoid_: baja, soft delete, borrado de usuario como sinónimo de cierre.

**Cuenta en cierre**:
Una cuenta con un cierre pedido que todavía no terminó. Sigue pudiendo entrar y leer, pero no crea tareas nuevas.
_Avoid_: cuenta inactiva, cuenta bloqueada, cuenta suspendida.

**Retención de tareas**:
La marca que Tasks pone sobre un propietario en cierre para no aceptarle tareas nuevas. Se quita si el cierre se rechaza y se borra con las tareas si se completa.
_Avoid_: lock, bloqueo, lápida.

**Tarea (TaskItem)**:
Una actividad personal con título y estado de completitud que pertenece a un propietario.
_Avoid_: ticket, trabajo, recurso como sinónimo de tarea.

**Proyecto (Project)**:
Una agrupación de tareas con nombre, que pertenece a un propietario. Su nombre es único entre los proyectos del mismo propietario, sin distinguir mayúsculas. Está activo o archivado. Una tarea puede pertenecer a un proyecto, y solo se crea en un proyecto propio y activo.
_Avoid_: carpeta, lista, categoría, etiqueta como sinónimo de proyecto.

**Proyecto archivado**:
Un proyecto que salió de uso sin borrarse. Conserva sus tareas y sus contadores, no acepta tareas nuevas y no cambia de nombre hasta que se restaura.
_Avoid_: borrado, cerrado, inactivo.

**Permiso**:
Una capacidad nombrada para una acción sobre una clase de recursos. Puede permitir actuar sobre recursos ajenos.
_Avoid_: rol, permiso propio, permiso global como concepto distinto.

**Rol**:
Una agrupación de permisos asignada a un usuario. User, Admin y SuperAdmin son los roles del template. El último SuperAdmin no puede cerrar su cuenta: el cierre se rechaza.
_Avoid_: permiso, categoría de perfil.

**Sesión**:
Un acceso autenticado que vive en el servidor y se abre, se usa y se cierra de forma independiente de otros accesos del mismo usuario. Vence por tiempo absoluto o por inactividad. Revocar una sesión es cerrarla desde el servidor; no cierra las otras sesiones del usuario.
_Avoid_: identidad, cuenta, token.
