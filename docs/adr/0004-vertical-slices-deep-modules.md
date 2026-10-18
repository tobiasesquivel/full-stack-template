# ADR-0004: Vertical slices dentro de features como módulos profundos

- Estado: aceptada
- Fecha: 2026-10-16
- Decide: Tobi
- Reemplaza: ADR-0001, ADR-0002 y ADR-0003 (borrados; quedan en el historial de git)

## Contexto

Este repo es un laboratorio de aprendizaje. El dominio (tareas) es trivial a propósito. Lo que importa es el rigor técnico, y todo puede cambiar.

La arquitectura anterior (ADR-0003) organizaba el backend en features con un módulo parcial por feature. Solo `Tasks` seguía esa forma. `Users` y `Auth` concentraban todo en un archivo. Había código de features en `Shared/` e `Infrastructure/`. Las features usaban el `Domain` de otras.

La investigación está en [informe-vsa-deep-modules.md](../evolucion/informe-vsa-deep-modules.md), [informe-deep-mediators.md](../evolucion/informe-deep-mediators.md) e [informe-dotnet-empresarial.md](../evolucion/informe-dotnet-empresarial.md).

## Decisión

Usamos **Vertical Slice Architecture** (Jimmy Bogard) dentro de features que funcionan como **módulos profundos** (John Ousterhout).

1. **Feature = módulo profundo.** Cada capacidad vive en `backend/Features/<Feature>/`. Desde afuera solo se ve `Contracts/`. Todo lo demás es interno.
2. **Slice = un caso de uso, en su carpeta.** `Features/Tasks/CreateTask/` tiene un archivo por pieza: `CreateTaskRequest.cs`, `CreateTaskRequestValidator.cs`, `CreateTask.cs` (la operación: una clase propia con sus dependencias), `CreateTaskEndpoint.cs` (el adapter HTTP) y `CreateTaskTests.cs`. La carpeta junta el caso de uso; cada archivo tiene un solo tipo.
3. **Entre features, solo `Contracts`.** Ninguna feature usa el `Domain`, la `Persistence` ni las slices de otra.
4. **Auth y Users se usan solo por contrato, en las dos direcciones.** Auth usa `Users.Contracts` (crea y lee el perfil, arma la URL del usuario creado). Users usa `Auth.Contracts` (lee y borra la cuenta). Ninguna conoce el interior de la otra.
5. **Tipos en la slice, salvo lo compartido.** Un tipo que usa una sola slice vive en la carpeta de la slice. Lo que usa otra feature va a `Contracts/`, igual que las responses HTTP que comparten varias slices. Lo interno que comparten varias slices (errores, servicios como `Sessions`, mapeos) queda en la raíz de la feature o en una carpeta técnica propia (`Access/`). Así `Contracts/` sigue siendo lo único que ve otra feature.
6. **Lo que nombra una feature va a esa feature.** `Infrastructure/` queda para la fontanería técnica del host. `Shared/` queda como núcleo mínimo que no nombra ninguna feature. Cada feature se registra sola desde su `<Feature>Feature.cs`.
7. **Un solo proyecto .NET.** Dune hace cumplir los límites.
8. **El frontend sigue el mismo criterio.** Cada feature de Angular tiene su servicio, sus modelos y sus rutas. `core/` queda para lo transversal.
9. **Sin mediator ni bus por ahora.** El paso 25 del plan elige un bus. Hasta entonces, un gate neutral rechaza cualquier librería de bus o mediator.

## Cambios

- 2026-10-17 (paso 0.4), decide Tobi: la slice pasa de un archivo a una carpeta con un archivo por pieza (punto 2). Lo interno que comparten varias slices queda en la raíz de la feature, no en `Contracts/` (punto 5). Antes el punto 5 mandaba a `Contracts/` todo lo compartido, y eso chocaba con "desde afuera solo se ve `Contracts/`".
- 2026-10-18 (paso 4), decide Tobi: el punto 4 pasa de "una sola dirección" a "las dos direcciones, solo por contrato". El código ya usaba `Users.Contracts` desde Auth, y `architecture.md` lo describía así. El paso 4 lo hace visible: `RegisterUser` arma la URL del usuario con `Users.Contracts.UserEndpointNames`.

## Condiciones para que una slice siga siendo profunda

Una clase por slice no es *classitis* si se cumple todo esto:

- Ninguna otra feature ve una slice. Solo ve `Contracts/`.
- La slice hace el caso de uso completo. No se parte en pasos ni en capas de paso (servicio → repositorio).
- No hay una interface por slice.
- El conocimiento común baja al `Domain` de la feature. No se copia entre slices.

## Alternativas descartadas

- **Un módulo parcial por feature (ADR-0003).** La interfaz es chica, pero todas las slices comparten el constructor. Una dependencia nueva de una slice llega a todas. Choca con "mínimo acoplamiento entre slices".
- **Un proyecto por capa en cada módulo** (`Ventas.Api`, `Ventas.Domain`…). Organiza por capa técnica, lo opuesto a VSA.
- **Todo dentro de `Features/`, sin `Shared/` ni `Infrastructure/`.** Ninguna fuente primaria lo pide. Mezcla capacidades de negocio con fontanería.
- **MediatR.** Agrega una indirección invisible y, desde la v13, una licencia comercial.

## Consecuencias

- El código actual no cumple todavía esta decisión. La Fase 0 del [plan](../evolucion/plan.md) lo lleva a esta forma, paso por paso.
- Varias reglas de Dune cambian a propósito: DUNE0003, DUNE0004, DUNE0008 y el gate de bus. Cada cambio lleva su fixture válida y su violación deliberada.
- El paso 21 del plan queda absorbido en gran parte por el paso 0.5.
- Hay más archivos y carpetas. A cambio, cada caso de uso se entiende sin abrir otros.
