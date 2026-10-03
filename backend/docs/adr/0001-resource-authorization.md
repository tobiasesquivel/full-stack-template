# ADR-0001: Autorización de recursos — owner-or-permission sobre IPermissionService

## Status

Accepted

## Context

El template implementaba el mismo concepto de autorización de recursos tres veces, distinto cada
vez: (1) los handlers de `Tasks` filtraban `WHERE Id == id && AuthId == callerAuthId` → 404,
colapsando existencia y propiedad en un solo `WHERE` (un `SuperAdmin` con `Permissions.Tasks.Delete`
no podía borrar la tarea de otro); (2) `Users.Delete` hacía un `Forbid()` inline en el endpoint;
(3) los permisos viajaban como claims horneados en el JWT al login, sin revocación hasta expirar
el access token (15 min). Decidido en el ticket 09 del mapa `backend-template`, con el research 02
como base de hechos y una política corporativa del usuario como restricción adicional.

## Decision drivers

- Un solo lugar que responda "¿puede este usuario tocar *este* recurso?".
- El titular del permiso (ej. SuperAdmin) debe poder operar recursos ajenos.
- Los cambios de roles/permisos deben ser visibles en el **próximo request** (política corporativa).
- JWT mínimo: identidad; ni roles ni permisos en el token.
- Legibilidad para quien abre el template por primera vez (criterio del ticket 08).

## Decision

- **El chequeo de recurso vive en el handler**, después de cargar por `Id`: carga → 404 si no
  existe → `AuthorizeAsync(user, entidad, requirement)` → 403 si ni dueño ni permiso. Existencia y
  propiedad son chequeos estructuralmente separados; nunca más un `WHERE` que los combine.
- **Un solo requirement + handler genérico**: `OwnerOrPermissionRequirement(permiso)` y
  `AuthorizationHandler<TRequirement, TResource>` con OR interno (`OwnerId == sub || HasPermission`),
  registrados una vez. Las entidades participantes implementan `IOwnedResource { string OwnerId }`
  (`TaskItem.AuthId` se renombra a `OwnerId`).
- **`IPermissionService` cacheado como única fuente de runtime**: roles del usuario y claims de
  rol en cache (TTL corto como recuperación) + invalidación explícita (`InvalidateUser` en cambios
  de rol del usuario, `InvalidateRole` en cambios de claims de rol) → visibles en el próximo
  request. `IMemoryCache` por defecto; `IDistributedCache` documentado para despliegues
  multi-instancia. Los seeders definen el estado inicial; la base de datos es la fuente de verdad.
- **JWT de identidad pura**: `sub`/`jti`/`iat`/`iss`. `AuthResponse` sigue exponiendo roles y
  permisos al cliente (ahora desde el servicio).
- **Doctrina 404/403**: 404 = no existe; 403 = existe pero el llamador no es dueño ni tiene el
  permiso. El permiso puro (operación sin recurso) se exige como gate de endpoint
  (`.RequireAuthorization(perm)`).
- **Granularidad**: `Permissions.{Recurso}.{Acción}` sin comodines ni scopes Own/Any — el patrón
  owner-or-permission es el scope.

## Considered Options

- **Roles en el token (research 02)**: evita lecturas por request, pero un cambio de rol no es
  visible hasta re-emitir el token (15 min) — incompatible con "siguiente request" de la política
  corporativa. Rechazado.
- **`IClaimsTransformation` hidratando claims por request**: cumple parcialmente, pero añade una
  transformación por request aunque no haya chequeos y pone el principal como portador de
  permisos; `IPermissionService` inyectado en los handlers es más directo y es la lectura literal
  de la política ("los permisos serán obtenidos mediante un IPermissionService"). Rechazado.
- **404 uniforme para recursos ajenos** (ocultamiento de existencia): legítimo según OWASP, pero
  el template enseña los estados separados; un equipo productivo puede adoptarlo con una línea.
  Rechazado para el template.
- **Scopes explícitos `Tasks.Read.Own` / `Tasks.Read.Any`**: duplican en strings lo que el
  handler resuelve en código. Rechazado.
- **ReBAC/OpenFGA**: sobreingeniería sin jerarquías ni multi-tenancy (research 02). Rechazado.

## Consequences

- Migran al nuevo modelo: `JwtService`, `LoginUserCommandHandler`, `RegisterCommandHandler`,
  `RoleSeeder`, los 6 handlers de `Tasks` y `Users.Delete`.
- Todo punto futuro que mute roles o claims **debe** llamar a la invalidación correspondiente;
  la convención se documenta (hoy los únicos puntos son `Register` y `RoleSeeder`).
- Los tests de autorización pasan de inyectar claims por header a stubs de `IPermissionService`.
- Revocación de sesiones/tokens (versionado vs deny-list) queda diferida al ticket 11 (Superficie
  de autenticación); hoy cubre el refresh rotation existente.
