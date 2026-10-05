# Archivo histórico OpenSpec

Estos documentos conservan decisiones y reportes del backend anterior. Sus afirmaciones de build, runtime, seguridad o completitud pertenecen a esos reportes heredados. No se reprodujeron en esta copia y no verifican ADR-0003.

La fuente normativa activa es [ADR-0003](../../../docs/adr/0003-full-stack-template-dune.md) y `openspec/specs` en la raíz. Los documentos se movieron con su contenido histórico intacto. Rutas de código y menciones de convenciones antiguas dentro de ellos describen el estado de su fecha, no archivos vigentes.

## Supersesiones

| Evidencia conservada | Contrato vigente | Cambio de mecanismo o comportamiento |
|---|---|---|
| [Auth anterior](pre-0003-backend-contract/specs/auth/spec.md), [add-jwt-auth](2026-06-08-add-jwt-auth/spec.md) | [Auth](../../specs/auth/spec.md) | Refresh solo cookie, antiforgery también en login y registro, replay revoca familia real y JWT sin permisos. |
| [User anterior](pre-0003-backend-contract/specs/user-entity/spec.md) | [Users](../../specs/users/spec.md) | UserProfile separado, nombre canónico Identity y cascadas. |
| [GetById anterior](pre-0003-backend-contract/specs/users-get-by-id/spec.md), [add-users-get-by-id](add-users-get-by-id/proposal.md) | [Users](../../specs/users/spec.md) | Perfil no público, owner-or-permission, 404 antes de 403 y UserResponse completo autorizado. |
| [Transacciones anteriores](pre-0003-backend-contract/specs/database-transactions/spec.md), [implement-db-conventions](2026-06-08-implement-db-conventions/spec.md) | [Auth](../../specs/auth/spec.md), [arquitectura](../../../docs/architecture.md) | Módulos directos y transacciones explícitas sustituyen ambient Wolverine. |
| [codebase-reorganization](codebase-reorganization/proposal.md) | [Dune](../../specs/dune/spec.md), [convenciones](../../../docs/conventions.md) | Modelos y tests colocados junto a cada feature, una raíz OpenSpec, sin handlers ni partial controllers legacy. |
| [error-catalog](error-catalog/proposal.md) | [Tasks](../../specs/tasks/spec.md), [Auth](../../specs/auth/spec.md), [Users](../../specs/users/spec.md) | Se conserva ErrorOr. Se eliminan toggle, perfil ausente como 404 y revocación de todas las sesiones ante replay de una familia. |
| [Config backend anterior](pre-0003-backend-contract/config.yaml) | [Config raíz](../../config.yaml) | No Wolverine, límite de 400 líneas ni ausencia normativa de pruebas. PostgreSQL sustituye la descripción MySQL falsa de la config raíz anterior. |

Las carpetas anteriores conservan sus identificadores y sus reportes originales. `pre-0003-backend-contract` conserva las cuatro specs antes activas y la config del backend. No es una segunda raíz activa.
