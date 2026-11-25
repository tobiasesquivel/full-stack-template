# Backend de full-stack-template: recursos

## Knowledge

### Del repo (la fuente de verdad del diseño)
- `docs/adr/0004-vertical-slices-deep-modules.md`: por qué cada caso de uso es una carpeta y cada feature un módulo. Usar para: "¿dónde va esto?".
- `docs/conventions.md`, sección "Dónde va cada cosa": la forma exacta de una slice (`*Query`, `*QueryHandler`, `*Endpoint`, `Contracts/`, `Access/`, `MessageNames/`). Usar para: nombres y carpetas.
- `docs/architecture.md`: los dos procesos (monolito y Tasks), el broker y la saga del cierre. Usar para: la vista macro.
- `docs/evolucion/investigacion/paso-29-1-wolverine-mediator.md`: cómo y por qué Wolverine hace de mediator. Usar para: lecciones 1 a 4.
- `docs/evolucion/investigacion/paso-25-broker-wolverine.md`, `paso-26-saga-coreografiada.md`, `paso-27-saga-orquestada.md`, `paso-28-durable-execution.md`: RabbitMQ, sagas y el período de gracia. Usar para: el bloque de mensajes.
- `docs/evolucion/investigacion/paso-23-2-extraer-tasks.md`: por qué Tasks salió a su propio proceso. Usar para: la lección de Hosts.

### Externos (primarios)
- [Wolverine: "Wolverine as Mediator"](https://wolverinefx.net/tutorials/mediator.html): `IMessageBus.InvokeAsync` busca el handler del mensaje y lo corre en línea, y devuelve su respuesta. Fuente principal de la lección 1.
- [Wolverine: descubrimiento de handlers](https://wolverinefx.net/guide/handlers/discovery.html) y [generación de código](https://wolverinefx.net/guide/codegen.html): por qué existe `Internal/Generated/`.
- [Jimmy Bogard: "Vertical Slice Architecture" (2018)](https://www.jimmybogard.com/vertical-slice-architecture/): "Minimize coupling between slices, and maximize coupling in a slice." Usar para: el porqué de las slices.
- [Microsoft Learn: parameter binding en minimal APIs (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding?view=aspnetcore-10.0): de dónde salen `authId`, `ClaimsPrincipal`, `IMessageBus` y `CancellationToken` en un endpoint.
- [Microsoft Azure Architecture Center: patrón Saga](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga) y [transacción compensatoria](https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction): qué es una saga, coreografía frente a orquestación.
- [microservices.io: Saga](https://microservices.io/patterns/data/saga.html) (Chris Richardson): la misma idea desde microservicios.
- [Wolverine: sagas](https://wolverinefx.net/guide/durability/sagas.html) y [Wolverine con RabbitMQ](https://wolverinefx.net/guide/messaging/transports/rabbitmq/): cómo lo implementa la librería que usa el repo.
- [RabbitMQ: colas](https://www.rabbitmq.com/docs/queues): qué guarda el broker y hasta cuándo.
- [Ousterhout, "A Philosophy of Software Design", extracto](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf): módulos profundos, la otra mitad de ADR-0004.

## Wisdom (Communities)
- Sin verificar todavía. Ver Gaps.

## Gaps
- Falta una comunidad verificada para preguntar sobre Wolverine y sagas en .NET. Candidata a revisar: las discusiones del repo de Wolverine en GitHub.
