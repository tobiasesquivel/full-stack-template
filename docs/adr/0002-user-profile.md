# ADR-0002: Identidad y perfil de usuario separados — `UserProfile` como puente al dominio

## Status

Accepted

## Supersesión parcial

[ADR-0003](0003-full-stack-template-dune.md) conserva el perfil separado, el nombre de login canónico en Identity, `Auth.ProfileMissing` y las cascadas. Sustituye la transacción ambient de Wolverine por una transacción explícita en `AuthModule`. El contrato vigente usa `AuthResponse.User`, no `UserInfo`. Las consecuencias originales siguientes son evidencia histórica, no instrucciones vigentes.

## Context

El template mantenía dos representaciones del usuario sincronizadas a mano: `AppIdentityUser :
IdentityUser` (sin campos extra) y `Domain/Entities/User` (POCO con `AuthId`, FK a
`AspNetUsers.Id`, y `Username`). `AppDbContext` shadoweaba con `new` el `DbSet<User> Users` de
`IdentityDbContext` — una colisión de nombres deliberada pero peligrosa: `_db.Users` no significa
lo que un lector de Identity espera. La FK solo existía en una dirección (`Users.AuthId →
AspNetUsers.Id`), así que "todo identity user tiene su fila de usuario" dependía de la
transacción de `RegisterCommandHandler`, no del esquema; y el login fallaba con un 404 engañoso
(`Auth.UserNotFound`) cuando esa fila faltaba. Decidido en el ticket 10 del mapa `backend-template`
por grilling con el usuario.

## Decision drivers

- El repo es un **template**: demuestra modularidad y escalabilidad, no eficiencia (criterio del
  usuario). La tabla del usuario debe anticipar datos que lo diferencien de la plomería de
  Identity.
- La convención del repo exige que `Domain/` no tenga referencias a framework.
- Un solo punto de verdad para el nombre de login (sin deuda de sincronización).
- Garantías de consistencia sin abstracciones nuevas: la transacción ya existe.
- Legibilidad para quien abre el template por primera vez.

## Decision

- **Separación, renombrada**: `Domain/Entities/User` pasa a ser `UserProfile` (DbSet
  `UserProfiles`). El `new DbSet<User> Users` desaparece. La justificación es de producto: una
  app creada desde el template tendrá datos de usuario que no son de identidad.
- **Campos**: se elimina `Username` del perfil — `AspNetUsers.UserName` es la fuente canónica y
  los queries lo leen de Identity. Entran `DisplayName` (poblado con el nombre de registro) y
  `Timezone` (IANA, default `UTC`), elegidos porque tienen comportamiento en una app de Tasks
  (assignee visible; deadlines zona-dependientes). El perfil es editable.
- **Consistencia — puerto único sin servicio**: `RegisterCommandHandler` crea la fila de
  identidad y la de perfil dentro de la transacción ambient de Wolverine; la convención se
  documenta en `AspNetConventions.md` (la creación de usuarios vive solo en el feature de
  registro; todo nuevo camino la extiende). El login es estricto: perfil ausente →
  `Auth.ProfileMissing` (error interno, ruidoso), no un 404.
- **Propiedad**: `TaskItem.OwnerId` apunta por FK a `UserProfile.AuthId`. `UserProfile` es el
  único puente dominio→framework; la cadena de cascada es `AspNetUsers → UserProfile →
  TaskItem`. El claim `sub` del JWT (id de identidad) coincide con `UserProfile.AuthId`, así que
  el handler owner-or-permission compara sin joins.
- **Contrato de auth**: `AuthResponse.UserInfo` gana `DisplayName` en registro y login.
- **Delete**: borrar la identidad cascadea perfil, refresh tokens y tareas; se eliminan los
  deletes manuales del handler.

## Considered Options

- **Fusión** (extender `AppIdentityUser` y borrar el espejo): elimina el shadowing, la
  duplicación y el join de login con una migración trivial (`DROP TABLE Users`). Rechazado: el
  usuario lo descartó explícitamente — un template debe anticipar datos de usuario que no son de
  identidad, y la fusión los obliga a vivir en la clase de Identity.
- **Servicio de provisión** (`UserProvisioningService` para identidad + perfil): el patrón
  "bien hecho" estándar. Rechazado por el usuario: la transacción en el handler es la garantía;
  el servicio era indirección sin segundo consumidor.
- **`Username` duplicado mantenido**: deuda de sincronización sin beneficio; con `DisplayName`
  entrando, el duplicado no aporta nada. Rechazado.
- **Login tolerante** (perfil ausente → identidad pura): suaviza la garantía y oculta la deriva.
  Rechazado: la deriva debe ser ruidosa.
- **Trigger de BD** para auto-crear el perfil: oculta la lógica en el esquema; un template que se
  lee como referencia no debe esconder invariantes ahí. Rechazado.
- **`TaskItem.OwnerId → AspNetUsers.Id`** (propiedad directa a identidad): lo que hacen la
  mayoría de los sistemas; eficiente. Rechazado bajo el criterio de modularidad: deja el esquema
  de dominio amarrado al framework y no demuestra la capa. Con el mismo valor en runtime
  (mismo string), el costo de la opción elegida es solo un eslabón más en la cadena de cascada.

## Consequences

- Una migración: `Users` → `UserProfiles` (renombra la FK a `FK_UserProfiles_AspNetUsers_AuthId`),
  backfill `DisplayName` desde el `Username` legacy, drop `Username`, add `Timezone` = `UTC`.
- Migran: `RegisterCommandHandler`, `LoginUserCommandHandler`, `GetUserByIdQueryHandler`,
  `DeleteUserCommandHandler`, `UserConfiguration`, `AppDbContext`, `Features/Users/*`.
- `TaskItemConfiguration` cambia el destino de su FK al completarse el ticket 02 del backlog
  (renombre `AuthId` → `OwnerId`).
- El contrato `AuthResponse.UserInfo` gana `DisplayName` — consumidores del template incluidos.
- El nuevo error `Auth.ProfileMissing` se suma al catálogo de errores.
- `AspNetConventions.md` se actualiza (entidad, puerto de creación, doctrina de perfil ausente,
  ejemplos a `UserProfiles`); `CONTEXT.md` gana el término "Perfil de usuario".
- La demostración del `Timezone` (un `DueDate` en `TaskItem` interpretado en la zona del dueño)
  queda escalada al ticket 19 (alcance de la golden slice).
