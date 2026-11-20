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
4. **Auth y Users se usan solo por contrato, en las dos direcciones.** Auth usa `Users.Contracts` (crea y lee el perfil, arma la URL del usuario creado). Users usa `Auth.Contracts` (lee la cuenta y recibe la decisión del cierre de cuenta como evento). Ninguna conoce el interior de la otra.
5. **Tipos en la slice, salvo lo compartido.** Un tipo que usa una sola slice vive en la carpeta de la slice. Lo que usa otra feature va a `Contracts/`, y nada más: una response HTTP que solo usa su feature queda en la slice o en la raíz de la feature. Lo interno que comparten varias slices (errores, servicios como `Sessions`, mapeos) queda en la raíz de la feature o en una carpeta técnica propia (`Access/`). Así `Contracts/` sigue siendo lo único que ve otra feature.
6. **Lo que nombra una feature va a esa feature.** `Infrastructure/` queda para la fontanería técnica del host. `Shared/` queda como núcleo mínimo que no nombra ninguna feature. Cada feature se registra sola desde su `<Feature>Feature.cs`.
7. **Un solo proyecto .NET.** Dune hace cumplir los límites. Desde el paso 23, un proyecto por proceso: lo cambia [ADR-0005](0005-gateway-y-procesos-propios.md).
8. **El frontend sigue el mismo criterio.** Cada feature de Angular tiene su servicio, sus modelos y sus rutas. `core/` queda para lo transversal.
9. **Sin mediator; un bus como transporte y para la saga orquestada.** Desde el paso 25 el bus es Wolverine sobre RabbitMQ y vive en `Infrastructure/Messaging`: lleva eventos de integración y comandos entre procesos. Desde el paso 27 también guarda la saga orquestada del cierre de cuenta: el archivo del orquestador (una clase que hereda `Wolverine.Saga`, y su `TimeoutMessage`) es el único lugar de una feature que nombra Wolverine. Ninguna slice lo usa como mediator (DUNE0025).

## Cambios

- 2026-10-17 (paso 0.4), decide Tobi: la slice pasa de un archivo a una carpeta con un archivo por pieza (punto 2). Lo interno que comparten varias slices queda en la raíz de la feature, no en `Contracts/` (punto 5). Antes el punto 5 mandaba a `Contracts/` todo lo compartido, y eso chocaba con "desde afuera solo se ve `Contracts/`".
- 2026-10-18 (paso 4), decide Tobi: el punto 4 pasa de "una sola dirección" a "las dos direcciones, solo por contrato". El código ya usaba `Users.Contracts` desde Auth, y `architecture.md` lo describía así. El paso 4 lo hace visible: `RegisterUser` arma la URL del usuario con `Users.Contracts.UserEndpointNames`.
- 2026-11-03 (paso 6.3), decide Tobi: `Contracts/` es solo lo que usa otra feature (punto 5). Las responses HTTP que comparten varias slices de la misma feature salen de `Contracts/` a la raíz de la feature (`TaskResponse`, `HealthStatusResponse`). Cada feature que expone algo tiene una sola puerta, `I<Feature>Module`, implementada por `<Feature>Module` en la raíz. No es el módulo parcial que descarta este ADR: es una interfaz por caso de uso entre features, y las slices siguen siendo clases propias.
- 2026-11-18 (paso 23), decide Tobi: el punto 7 pasa a "un proyecto .NET por proceso". El gateway del paso 23 es un proceso propio, y el paso 23.2 extrae `Tasks` a otro. Lo explica [ADR-0005](0005-gateway-y-procesos-propios.md).
- 2026-11-19 (paso 25), decide Tobi la herramienta (Wolverine y RabbitMQ) y el modo autónomo el resto: el punto 9 pasa de "sin mediator ni bus por ahora" a "sin mediator; un bus solo como transporte". Wolverine entra solo por `Infrastructure/Messaging` (gate de `check-structure.mjs` y DUNE0025); las features publican con `IntegrationEventPublisher` y consumen con `IIntegrationEventHandler<T>`, interfaces del repo. `IMessageBus.InvokeAsync`, el modo mediator de Wolverine, está prohibido en todo el producto.
- 2026-11-20 (paso 26), modo autónomo: el punto 4 pasa de "Users lee y borra la cuenta" a "Users lee la cuenta y recibe la decisión del cierre como evento". Borrar la cuenta es una saga coreografiada (Users, Tasks y Auth); Auth borra la identidad cuando decide el cierre y `IAuthModule.DeleteAccountAsync` se borra. Users consume `auth.account-deleted` y `auth.account-closure-rejected` con los tipos `AccountDeletedInfo` y `AccountClosureRejectedInfo` de `Auth.Contracts`, porque Wolverine acepta un solo tipo por nombre dentro de un proceso.
- 2026-11-20 (paso 27), decide Tobi la herramienta (`Wolverine.Saga` con el outbox durable de Wolverine y código generado versionado) y el modo autónomo el resto: el punto 9 suma "y para la saga orquestada". El orquestador `AccountClosureSaga` vive en `Users/OrchestrateAccountClosure` y nombra Wolverine; las demás slices siguen sin conocerlo. Wolverine solo descubre handlers públicos, así que la saga y los tipos de su firma son `public` fuera de `Contracts/` (DUNE0020 lo acepta solo para ellos; DUNE0004 sigue impidiendo que otra feature los use). El punto 4 no cambia: Users sigue recibiendo la decisión de Auth como evento, ahora en la saga.

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
