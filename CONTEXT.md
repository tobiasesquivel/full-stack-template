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
El proceso que borra una cuenta en pasos. Primero corre el período de gracia; al vencer, Users la marca en cierre, Tasks retiene sus tareas y Auth decide si la borra. Si Auth la rechaza, los pasos anteriores se deshacen, primero en Tasks y después en Users, y la cuenta vuelve a estar activa con el motivo del rechazo. Lo identifica un id de cierre que el usuario recibe al pedirlo y que no cambia en todo el proceso. Lo coordina el orquestador del cierre.
_Avoid_: baja, soft delete, borrado de usuario como sinónimo de cierre.

**Período de gracia**:
El plazo entre el pedido de cierre y el inicio del cierre (14 días por defecto), en el que el cierre está agendado y todavía se puede cancelar. La cuenta funciona igual que antes. Recibe dos recordatorios (días 7 y 13) y, al vencer, arranca el cierre. Cancelarlo devuelve la cuenta a activa sin tocar Tasks ni Auth.
_Avoid_: cooldown, demora, cierre pendiente como sinónimo; un cierre cancelado no es un cierre rechazado.

**Historial del cierre**:
La lista ordenada de los hechos de un cierre: pedido, recordatorios, cancelación, vencimiento del período de gracia, inicio del cierre y resultado. Cada hecho dice cuándo pasó y quién lo hizo (el id opaco de quien pidió o canceló, o el sistema), sin datos personales. Solo se agregan hechos: ninguno se cambia ni se borra, y sobrevive al borrado de la cuenta.
_Avoid_: log, auditoría (la auditoría registra cambios de entidades), event store.

**Orquestador del cierre**:
Quien coordina un cierre de cuenta: guarda en qué estado está cada cierre (reteniendo tareas, decidiendo, soltando tareas, completado o rechazado), le pide a Tasks y a Auth el paso siguiente y sigue desde ese estado si el sistema se reinicia. Un cierre que pasa su plazo sin terminar queda marcado como atrasado.
_Avoid_: workflow, manager, coordinador como clase genérica.

**Cuenta en cierre**:
Una cuenta cuyo cierre ya arrancó (pasó el período de gracia) y todavía no terminó. Sigue pudiendo entrar y leer, pero no crea tareas nuevas. Durante el período de gracia la cuenta está agendada, no en cierre.
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
