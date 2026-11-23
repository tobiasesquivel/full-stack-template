# Paso 29.1: Wolverine como mediator (investigación)

- Fecha: 2026-11-23.
- Estado: **Listo** como investigación. Quedan abiertas dos decisiones de herramienta para Tobi (sección "Herramientas"); el paso se puede hacer sin ninguna de las dos.
- Alcance: el paso nuevo "29.1: Wolverine como mediator" que pidió Tobi. Las slices pasan a ser handlers de Wolverine y los endpoints les despachan con `IMessageBus.InvokeAsync<T>`. El repo existe para aprender arquitectura .NET empresarial: el patrón entra aunque un CRUD no lo necesite (`AGENTS.md`, "Para qué existe").
- Base: `main` en `293f47e` (merge del PR #93, paso 29), rama `t3/wolverine-mediator-handlers`. Wolverine 6.64.2 ya instalado (`backend/Platform/TemplateApp.Platform.csproj:30-34`).
- Límites con los pasos vecinos: el paso no cambia contratos HTTP (las respuestas siguen iguales), no toca el outbox casero del paso 24 ni la saga del 27, y no adopta la validación ni las transacciones de Wolverine si no lo decide Tobi (regla 5).

**Cómo leer las marcas:**
- `[CÓDIGO archivo:línea]`: lo leí en el repo, en `293f47e`.
- `[FUENTE]`: lo leí en un documento primario: la documentación de Wolverine (wolverinefx.net, que se genera desde `docs/` del repo) y el código de Wolverine, los dos en la etiqueta `V6.64.2`; el blog de Jeremy Miller; el código de ErrorOr `v2.1.1`. Cada uno tiene su link.
- `[INFERENCIA]`: deducción mía a partir de código o docs. No la medí.
- `[NO PROBADO]`: no lo corrí. Queda para el paso.

## Respuesta corta

1. **Cómo funciona (pregunta 1).** `InvokeAsync(msg)` busca el handler local del tipo del mensaje y lo ejecuta **en el mismo hilo del que llama**, esperando a que termine. `InvokeAsync<T>(msg)` además devuelve el valor de tipo `T` que retorna el handler, y ese valor **no** se publica como mensaje en cascada. Con un handler local, el `timeout` del método no se usa y el `CancellationToken` pasa directo al handler. Una excepción del handler llega al que llama, **salvo que una regla `Retry`/`RetryWithCooldown` la reintente adentro de la llamada**. Lo que el handler devuelva además del `T` se publica después del handler, sin outbox salvo que el middleware transaccional de EF esté activo.
2. **Cómo encaja con la configuración de hoy (pregunta 2).** El descubrimiento por convención sigue apagado: cada handler se registra con `options.Discovery.IncludeType(tipo)`, como ya se hace con las sagas. El código generado se versiona igual que en el paso 27. Wolverine rechaza un handler `internal` de primer nivel; un handler **público anidado dentro de una clase `internal`** pasa ese control y DUNE0020 no lo mira, pero no está probado de punta a punta. El host de Tasks hoy no tiene código generado, ni comando `codegen`, ni `ServiceLocationPolicy`: necesita los tres.
3. **Errores sin excepciones (pregunta 3).** Sí: un handler puede devolver `ErrorOr<TaskResponse>` y el endpoint pedir `InvokeAsync<ErrorOr<TaskResponse>>`. Trampa: si el handler no produce ese valor (una cadena cortada por un `Validate`, un handler distinto), `InvokeAsync<T>` devuelve `default`, y el `default` de `ErrorOr` es un **éxito con valor `null`**. No hay que registrar `ErrorOr` con `UseResultType`, porque eso convierte los errores en strings y en una excepción.
4. **Middleware (pregunta 4).** Choca con tres cosas del repo: las **reglas globales de reintento** se aplicarían a cada `POST` (hasta 36 s de espera con un `NpgsqlException`), el **scope de DI** del handler no es el del request (se pierden los permisos que `RemoteSession` guarda en `TaskAccess`), y en el monolito `AutoApplyTransactions` ya está prendido y le abriría una transacción de EF a cualquier handler que dependa de un `DbContext`. La validación con FluentValidation y los endpoints de Wolverine piden paquetes nuevos: los decide Tobi.
5. **Estilo de endpoint (pregunta 5).** Minimal API con `IMessageBus.InvokeAsync<T>` no suma paquetes y deja intactos `ApiResults`, `WithIdempotency` y el versionado. Wolverine.HTTP (`WolverineFx.Http`) es lo que recomienda el equipo de Wolverine, pero cambia el modelo de endpoints entero. Decide Tobi.
6. **Tests (pregunta 6).** Los tests HTTP de las slices no cambian: el contrato es el mismo. Se suma un test por handler llamado directo, y uno que fija que un fallo no se reintenta. Las sesiones rastreadas (`InvokeMessageAndWaitAsync`) son del núcleo de Wolverine y sirven si el handler publica algo.
7. **Guía profesional (pregunta 7).** Jeremy Miller: `InvokeAsync` = `IMediator.Send` de MediatR; nunca invocar un handler desde otro handler; publicar solo desde el handler raíz; preferir funciones puras (A-Frame); el mediator detrás de un endpoint es un caso "que soportamos del todo" pero no el preferido.
8. **Propuesta (pregunta 8).** Migrar **solo las tres escrituras HTTP de Tasks** (`CreateTask`, `UpdateTask`, `DeleteTask`): un proceso, sin `AutoApplyTransactions`, con tests que ya cubren el contrato. Reescribir el punto 9 de ADR-0004 y DUNE0025 para permitir `IMessageBus.InvokeAsync<T>` solo en el `*Endpoint.cs` de una slice, y sumar DUNE0028: "el endpoint de escritura despacha su propio comando por el mediator".

## Glosario corto

- **Mediator** (= un despachador en el medio): el endpoint no llama a la clase del caso de uso; le pasa un mensaje a un despachador y este busca el handler. "Wolverine's `IMessageBus.InvokeAsync()` is the direct equivalent to MediatR's `IMediator.Send()`" ([FUENTE: Jeremy Miller, Wolverine for MediatR Users](https://jeremydmiller.com/2025/01/28/wolverine-for-mediatr-users/)).
- **Handler** (= el método que atiende un tipo de mensaje): un método `Handle`/`HandleAsync`/`Consume`/`ConsumeAsync` público; "Wolverine assumes that the first argument is the incoming message" ([FUENTE: Wolverine, Discovery](https://wolverinefx.net/guide/handlers/discovery.html)).
- **Comando** (= el mensaje que pide un caso de uso): un record con los datos del pedido. Acá = `CreateTaskCommand`.
- **Mensaje en cascada** (= lo que devuelve un handler y Wolverine publica solo): "a message 'cascaded' from a handler is effectively the same thing as calling `IMessageBus.PublishAsync()`" ([FUENTE: Wolverine, Message Handlers](https://wolverinefx.net/guide/handlers/)).
- **Cadena de handler** (= el código que Wolverine genera alrededor del handler): middleware, transacción, reglas de error y la llamada al método. Acá vive en `backend/Internal/Generated/WolverineHandlers/` `[CÓDIGO]`.
- **Service location** (= pedir una dependencia al contenedor en tiempo de ejecución en vez de construirla en el código generado): Wolverine la usa cuando una dependencia scoped es `internal` ([FUENTE: Wolverine, Code Generation](https://wolverinefx.net/guide/codegen.html#wolverine-code-generation-and-ioc)).
- **A-Frame** (= separar lógica, infraestructura y coordinación): el handler principal queda como "a pure function" y Wolverine actúa "as the mediator between the infrastructure concerns" ([FUENTE: Jeremy Miller, A-Frame Architecture with Wolverine](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/)).

---

## 1. Cómo funciona el modo mediator en Wolverine 6.x

### 1.1 `InvokeAsync` y `InvokeAsync<T>`

- **Firmas.** `Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = default)` y `Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = default)`, más variantes con `DeliveryOptions` y `InvokeForTenantAsync` ([FUENTE: código, `IMessageBus.cs:107-146`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/IMessageBus.cs#L107-L146)).
- **En línea, en el hilo del que llama.** "If the `Message1` message has a local subscription, the message handler will be invoked in the calling thread" ([FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)). En el código, `MessageBus.InvokeAsync` busca el invocador por el tipo del mensaje (`Runtime.FindInvoker(message.GetType())`) y lo ejecuta ([FUENTE: código, `MessageBus.cs:117-140`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/MessageBus.cs#L117-L140)).
- **La respuesta.** `InvokeAsync<T>` marca el sobre con `ResponseType = typeof(T)` y `DoNotCascadeResponse = true`; al terminar devuelve `envelope.Response`, y **si es `null` devuelve `default`** ([FUENTE: código, `Executor.cs:178-210`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L178-L210)). La respuesta sale del valor que retorna el handler: "this only works if the message handler method returns a cascading message of the exact same type" ([FUENTE: Wolverine, Mediator tutorial](https://wolverinefx.net/tutorials/mediator.html)), y "the reply type of `T` **is not also published as a cascaded message**" ([FUENTE: Wolverine, Cascading](https://wolverinefx.net/guide/handlers/cascading.html)). El código lo confirma: si el valor es del tipo pedido se guarda en `Envelope.Response` y, con `DoNotCascadeResponse`, no se publica ([FUENTE: código, `MessageContext.cs:1102-1128`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/MessageContext.cs#L1102-L1128)).
- **No hay control de "faltó la respuesta" en el modo en línea.** `AssertAnyRequiredResponseWasGenerated` se saltea cuando el canal es `InvocationCallback`, que es el de `InvokeAsync` ([FUENTE: código, `MessageContext.cs:544-548`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/MessageContext.cs#L544-L548)). Entonces un handler que no devuelve `T` no falla: el que llama recibe `default` `[INFERENCIA]` (ver 3.2).

### 1.2 Convenciones de handler

- Tipos: públicos, concretos, cerrados; nombre terminado en `Handler`/`Consumer`, o `IWolverineHandler`, o `[WolverineHandler]` (obligatorio para clases estáticas en el descubrimiento por convención) ([FUENTE: Wolverine, Discovery](https://wolverinefx.net/guide/handlers/discovery.html)). Con el descubrimiento apagado, `IncludeType` acepta una clase estática sin atributo ([FUENTE: código, `HandlerDiscovery.cs:415-432`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Configuration/HandlerDiscovery.cs#L415-L432)).
- Métodos: públicos, de instancia o estáticos (`GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)`) ([FUENTE: código, `HandlerDiscovery.cs:385`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Configuration/HandlerDiscovery.cs#L385)), con nombre `Handle`, `Handles`, `Consume`, `Consumes` y su versión `Async` ([FUENTE: Wolverine, Discovery](https://wolverinefx.net/guide/handlers/discovery.html)).
- Inyección por método: cualquier servicio del contenedor, `Envelope`, la hora actual y otras fuentes de codegen ([FUENTE: Wolverine, Message Handlers](https://wolverinefx.net/guide/handlers/)). Un handler estático ahorra crear el objeto: "Using a static method as your message handler can be a small performance improvement" (misma fuente).
- Handlers compuestos: métodos `Before`/`Load`/`Validate` corren antes del `Handle`; `After`/`PostProcess` después; `Finally` en el `finally` (misma fuente, "Compound Handlers").

### 1.3 Cancelación, timeout y scope

- **Cancelación.** En el camino en línea, el `CancellationToken` del que llama pasa directo a `Handler.HandleAsync`; el `CancelAfter(_timeout)` solo está en `ExecuteAsync`, el camino de una cola ([FUENTE: código, `Executor.cs:142-175` y `Executor.cs:226-251`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L142-L251)). Con el `ct` del endpoint (`HttpContext.RequestAborted`), un cliente que corta cancela el handler `[INFERENCIA]`.
- **Timeout.** "Wolverine does enforce timeout conditions with a default of 5 seconds" vale para la invocación **remota** ([FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html)). En el camino local el parámetro `timeout` llega a `Executor.InvokeAsync` y no se usa ([FUENTE: código, `Executor.cs:212-226`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L212-L226)).
- **Scope de DI.** El código generado construye las dependencias con `new` cuando puede, y si alguna dependencia scoped es `internal` pide todo a un scope **propio** (`IServiceScopeFactory`) ([FUENTE: Wolverine, Code Generation](https://wolverinefx.net/guide/codegen.html#wolverine-code-generation-and-ioc)). El repo ya lo muestra: el handler generado de la saga de gracia abre `_serviceScopeFactory.CreateAsyncScope()` y pide `UsersDbContext` porque "is not public, so requires service location" `[CÓDIGO backend/Internal/Generated/WolverineHandlers/AccountClosureGraceExpiredHandler503767472.cs:24-36]`. En los dos casos **no es el scope del request HTTP**. Solo Wolverine.HTTP ofrece usar `HttpContext.RequestServices` ([FUENTE: Wolverine HTTP, Using the HttpContext.RequestServices](https://wolverinefx.net/guide/http/#using-the-httpcontext-requestservices)).

### 1.4 Excepciones y reglas de error con `InvokeAsync`

- "When using `IMessageBus.InvokeAsync()` to execute a message inline, only the 'Retry' and 'Retry With Cooldown' error policies are applied to the execution **automatically**" ([FUENTE: Wolverine, Error Handling](https://wolverinefx.net/guide/handlers/error-handling.html)).
- El código: si el handler tira, busca una continuación en línea en las reglas; si no hay, relanza al que llama; si hay, descarta los mensajes del intento fallido y reintenta ([FUENTE: código, `Executor.cs:292-330`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L292-L330)). Las reglas de la cadena van primero y después las globales, y una regla que coincide pero no es "en línea" (mover a la DLQ, reencolar) **se saltea y se sigue buscando** ([FUENTE: código, `FailureRuleCollection.cs:26-100`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/ErrorHandling/FailureRuleCollection.cs#L26-L100)).
- Acciones propias en línea: `CustomAction(..., InvokeResult.Stop)` o `TryAgain` ([FUENTE: Wolverine, Custom Actions for InvokeAsync](https://wolverinefx.net/guide/handlers/error-handling.html#custom-actions-for-invokeasync)).
- **Choque con el repo:** las reglas del paso 25 son globales (`options.Policies`): `NpgsqlException` y `TimeoutException` se reintentan a 1 s, 5 s y 30 s, y cualquier otra excepción a 200 ms y 1 s `[CÓDIGO backend/Infrastructure/Messaging/MessagingExtension.cs:57-64]`. Detrás de un `POST`, un `NpgsqlException` dejaría el request colgado unos 36 s y repetiría la llamada a Projects; y un fallo dentro del `TransactionScope` del filtro de idempotencia reintentaría sobre una transacción ya abortada `[INFERENCIA]`. El paso tiene que aislar esas reglas de los handlers del mediator (sección 4.1).

### 1.5 Mensajes en cascada y outbox

- Lo que el handler devuelve además del `T` pedido se publica al terminar (`FlushOutgoingMessagesAsync` después del handler) ([FUENTE: código, `Executor.cs:155-159`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L155-L159)).
- Con el middleware de EF, "these outgoing messages are held until the ongoing transaction completes" ([FUENTE: Wolverine, Mediator tutorial](https://wolverinefx.net/tutorials/mediator.html)), y el `DbContext` "is used to persist the outgoing messages as part of Wolverine's outbox support" ([FUENTE: Wolverine, EF Core Transactional Middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html)).
- **En este repo:** el host de Tasks no configura persistencia ni transacciones de Wolverine (`AddMessaging()` sin `storesSagas`) `[CÓDIGO backend/Hosts/Tasks/Program.cs:17]`; solo el monolito llama `SagaStorage.Configure` `[CÓDIGO backend/Infrastructure/Messaging/MessagingRegistration.cs:22-23]`. Un mensaje en cascada desde un handler de Tasks saldría **sin outbox** `[INFERENCIA]`. Los eventos siguen saliendo como hoy: la entidad los anota y el outbox casero los guarda en el mismo `SaveChanges` (`conventions.md`, "Eventos y outbox"). Regla del paso: un handler del mediator devuelve solo su respuesta, sin cascada.

## 2. Cómo encaja con la configuración actual

### 2.1 Descubrimiento apagado y registro explícito

- Hoy: `TypeLoadMode.Static` y `DisableConventionalDiscovery()` `[CÓDIGO MessagingExtension.cs:53-54]`; los eventos de integración van por `AddMessageHandler` sin codegen `[CÓDIGO MessagingExtension.cs:80]`; las sagas por `options.Discovery.IncludeType(saga.ClrType)` `[CÓDIGO MessagingExtension.cs:97]`, a partir de un `SagaType` que cada feature registra con `AddSaga<TSaga, TContext>()` `[CÓDIGO MessagingRegistration.cs:54-60]`.
- Para el mediator, el mismo camino que las sagas: un `AddMediatorHandler<THandler>()` en `MessagingRegistration` que registra un `MediatorHandlerType`, y `MessagingExtension` que llama `IncludeType` por cada uno. La feature lo llama desde su `<Feature>Feature.cs`, sin nombrar Wolverine `[INFERENCIA]`.
- Ojo con las reglas globales de deshabilitar: "disabling conventional discovery will *also* disable any customizations you may have made to the conventional handler discovery" ([FUENTE: Wolverine, Discovery](https://wolverinefx.net/guide/handlers/discovery.html)). No aplica: el repo no tiene personalizaciones.

### 2.2 Handlers públicos frente a slices `internal`

- Wolverine rechaza un tipo de primer nivel no público: `IncludeType` tira "Handler types must be public, concrete, and closed (not generic) types" si `type.IsNotPublic` ([FUENTE: código, `HandlerDiscovery.cs:417-421`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Configuration/HandlerDiscovery.cs#L417-L421)). El paso 27 lo midió con la saga (`docs/decisions.tsv`, fila del 2026-11-20 "Wolverine rechaza una saga `internal`").
- No encontré una opción para descubrir tipos `internal`: el filtro de tipos de mensaje excluye los no públicos ([FUENTE: código, `HandlerDiscovery.cs:42`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Configuration/HandlerDiscovery.cs#L42)) y el registro generado filtra por `IsPublic` o `IsNestedPublic` ([FUENTE: código, `HandlerRegistry.cs:95-101`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/HandlerRegistry.cs#L95-L101)).
- **El problema real es C#, no Wolverine.** Un método público de un tipo público no puede tener parámetros `internal` (CS0051). Un handler público que pida `TasksDbContext` (internal, `conventions.md` "Persistencia") no compila. El paso 27 lo resolvió con una interfaz pública por slice (`IAccountClosureSteps`) `[CÓDIGO backend/Features/Users/OrchestrateAccountClosure/IAccountClosureSteps.cs:3]`, pero ADR-0004 pide "No hay una interface por slice" `[CÓDIGO docs/adr/0004-vertical-slices-deep-modules.md:46]`.
- **Salida candidata: un tipo público anidado dentro de una clase `internal`.**
  - `Type.IsNotPublic` es `false` para un tipo anidado, así que `IncludeType` lo acepta; el registro generado lo incluye por `IsNestedPublic` (mismas fuentes) `[INFERENCIA]`.
  - En C#, el dominio de accesibilidad de un público anidado en un `internal` es `internal`: puede tener parámetros `internal` sin CS0051 `[INFERENCIA]`.
  - DUNE0020 no mira tipos anidados: `if (type.ContainingType is not null) return;` `[CÓDIGO dune/Dune.Analyzers/PublicSurfaceAnalyzer.cs:34]`.
  - El código generado ya nombra tipos `internal` y compila (`GetRequiredService<TemplateApp.Features.Users.Persistence.UsersDbContext>`) `[CÓDIGO AccountClosureGraceExpiredHandler503767472.cs:36]`, así que `codegen write` tolera nombrar tipos internos `[INFERENCIA]`.
  - Choca con una convención: "Un tipo auxiliar ... se declara `file` o anidado y privado" `[CÓDIGO docs/conventions.md:12]`. Un anidado público es una excepción nueva que hay que escribir.
  - `[NO PROBADO]` de punta a punta. Es el primer commit del paso (sección 8.5). Si falla, el plan B es el adaptador sin codegen de la sección 8.2.

### 2.3 Código generado y `ServiceLocationPolicy`

- El monolito ya versiona el código generado en `backend/Internal/Generated/WolverineHandlers/` y `check-codegen.mjs` falla si quedó viejo `[CÓDIGO dune/check-codegen.mjs:6]`. Un handler nuevo del monolito suma un archivo ahí.
- "Wolverine 6.0 changes the IoC integration default: **`ServiceLocationPolicy.NotAllowed` is now the default**", y una dependencia opaca "throws `InvalidServiceLocationException` at startup" ([FUENTE: Wolverine, Code Generation](https://wolverinefx.net/guide/codegen.html)). Una dependencia scoped `internal` obliga a service location (misma fuente). El monolito ya fija `AlwaysAllowed` `[CÓDIGO backend/Infrastructure/Messaging/SagaStorage.cs:20]`, pero solo cuando guarda sagas.

### 2.4 Diferencias por proceso

| | Monolito (`backend/Program.cs`) | Tasks (`backend/Hosts/Tasks`) |
|---|---|---|
| Ensamblado de aplicación | `typeof(MonolithHost).Assembly` `[CÓDIGO Program.cs:19]` | **el de `Platform`**: `AddMessaging()` sin argumento cae en `typeof(MessagingRegistration).Assembly` `[CÓDIGO MessagingRegistration.cs:21]` |
| Comando `codegen` | `RunWithMessagingCommandsAsync` `[CÓDIGO Program.cs:54]` | no: `await app.RunAsync()` `[CÓDIGO Hosts/Tasks/Program.cs:47]` |
| Carpeta `Internal/Generated` compilada | sí (ítems por defecto del csproj) | no: `EnableDefaultCompileItems=false` y solo `*.cs` de la raíz `[CÓDIGO Hosts/Tasks/TemplateApp.Tasks.csproj:9-14]` |
| `ServiceLocationPolicy` | `AlwaysAllowed` | por defecto `NotAllowed` |
| `AutoApplyTransactions` + EF | sí `[CÓDIGO SagaStorage.cs:21-22]` | no |

Lo que implica para Tasks:
- `codegen write` escribe "relative to the host's content root" y `TypeLoadMode.Static` busca en `ApplicationAssembly`; si no coinciden, desde 6.30 "this mismatch fails the host start" ([FUENTE: Wolverine, Code Generation, troubleshooting](https://wolverinefx.net/guide/codegen.html#troubleshooting-code-generation-issues)). Tasks tiene que pasar `typeof(TasksHost).Assembly` (`backend/Hosts/Tasks/TasksHost.cs` ya existe).
- Sumar `RunWithMessagingCommandsAsync`, el `Compile Include` de `Internal/Generated/**/*.cs` y una segunda corrida en `check-codegen.mjs` (`--project backend/Hosts/Tasks/TemplateApp.Tasks.csproj`).
- Mover `ServiceLocationPolicy = AlwaysAllowed` de `SagaStorage` a `MessagingExtension`, para los dos procesos `[INFERENCIA]`.

## 3. Errores sin excepciones: `ErrorOr<T>` como respuesta

### 3.1 Funciona sin configurar nada

- Wolverine guarda como respuesta cualquier valor que sea instancia de `T` (`ResponseType.IsInstanceOfType(message)`) ([FUENTE: código, `MessageContext.cs:1111-1114`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/MessageContext.cs#L1111-L1114)). `ErrorOr<TValue>` es un `readonly partial record struct` ([FUENTE: ErrorOr v2.1.1, `ErrorOr.cs:11`](https://github.com/amantinband/error-or/blob/v2.1.1/src/ErrorOr/ErrorOr.cs#L11)); viaja en caja (`object`) y vuelve igual `[INFERENCIA]`.
- El endpoint queda igual que hoy: `ApiResults.ToResult(result, ...)` sobre el `ErrorOr<TaskResponse>` `[CÓDIGO backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs:19-20]`.
- `[NO PROBADO]`: el codegen con un handler que devuelve un struct genérico. Es parte del primer commit.

### 3.2 Trampas

- **`default` es un éxito vacío.** Si `envelope.Response` es `null`, `InvokeAsync<T>` devuelve `default!` (sección 1.1). En `ErrorOr`, `IsSuccess => _errors is null` ([FUENTE: ErrorOr v2.1.1, `ErrorOr.cs:38`](https://github.com/amantinband/error-or/blob/v2.1.1/src/ErrorOr/ErrorOr.cs#L38)), así que `default(ErrorOr<TaskResponse>)` es éxito con `Value == null`, y el endpoint respondería 201 sin cuerpo `[INFERENCIA]`. Pasa si una cadena se corta antes del handler (un `Validate` que devuelve mensajes: "Wolverine will log them and abort the handler execution" ([FUENTE: Wolverine, Validation](https://wolverinefx.net/guide/handlers/#validation))). Por eso la validación queda adentro del handler y devuelve `ErrorOr` (sección 4.2), y un test fija que ningún camino devuelve un éxito vacío.
- **No registrar `ErrorOr` con `UseResultType`.** Wolverine 6.x tiene `opts.UseResultType<TResult>(stopWhen, unwrapWith, errorsFrom)`: el fallo se loguea como strings y el que espera el tipo desenvuelto recibe una `ResultFailureException` ([FUENTE: código, `WolverineOptions.Results.cs:21-40`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/WolverineOptions.Results.cs#L21-L40), [`ResultUnwrappingActionSource.cs:27-60`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/ResultUnwrappingActionSource.cs#L27-L60)). Se perderían los `Error` tipados que `ApiResults` traduce al catálogo de errores (paso 6). Sin registrar, el `ErrorOr` pasa entero.
- **Si alguien llama `InvokeAsync` sin `<T>`**, el `ErrorOr` se trata como mensaje en cascada y se publica; sin suscriptor, `PublishAsync` lo ignora ([FUENTE: Wolverine, Message Bus](https://wolverinefx.net/guide/messaging/message-bus.html): publish "will ... ignore the message if there is no subscriber") y el resultado se pierde `[INFERENCIA]`. DUNE0028 exige la forma genérica (sección 8.4).
- **Choque con la guía de Wolverine:** "don't wrap handler return values in `Result<T>` types to carry validation failures around. Let Wolverine stop the processing and report the problem" ([FUENTE: Wolverine, Best Practices](https://wolverinefx.net/introduction/best-practices.html)). El repo usa `ErrorOr` en todas las slices y el catálogo de errores depende de eso; cambiarlo es otro paso. Lo anoto en "Choques".

## 4. Middleware y políticas que habilita el mediator

### 4.1 Reglas de error

- **Choque:** las reglas globales de reintento del paso 25 alcanzan a `InvokeAsync` (sección 1.4).
- **Arreglo, sin paquete:** que los handlers del mediator no reintenten. Dos formas, las dos en `Infrastructure/Messaging`:
  - (E1) una regla por cadena para cada handler del mediator con `CustomAction(..., InvokeResult.Stop)` que gane a las globales, porque las reglas de la cadena van primero ([FUENTE: código, `FailureRuleCollection.cs:26-37`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/ErrorHandling/FailureRuleCollection.cs#L26-L37)).
  - (E2) pasar las reglas del bus de globales a reglas por cadena, solo para los mensajes de integración, comandos y sagas.
  - Recomiendo (E2): las reglas del paso 25 eran para mensajes que llegan por una cola, y así quedan escritas para eso `[INFERENCIA]`. `[NO PROBADO]` el mecanismo exacto. El test del paso: un `NpgsqlException` en `CreateTask` responde 500 en menos de 1 s y el handler corre una sola vez.

### 4.2 Validación

| Opción | Paquete | Cómo falla | Choque con el repo |
|---|---|---|---|
| (V0) Inline en el handler, como hoy (`Validator.ValidateAsync` → `ValidationErrors.From`) `[CÓDIGO CreateTask.cs:32-34]` | ninguno | devuelve `ErrorOr` con errores de validación | ninguno |
| (V1) Método `Validate()` del handler compuesto | ninguno (núcleo) | loguea y corta la cadena; con `InvokeAsync<T>` → `default` ([FUENTE: Validation](https://wolverinefx.net/guide/handlers/#validation)) | éxito vacío (3.2) |
| (V2) `WolverineFx.FluentValidation` (`opts.UseFluentValidation()`) | **nuevo** | "the middleware will throw a `ValidationException` and stop all processing"; suma una regla que descarta el mensaje ([FUENTE: Fluent Validation Middleware](https://wolverinefx.net/guide/handlers/fluent-validation.html)) | flujo por excepción en vez de `ErrorOr`; "type scanning" de validadores; la regla global de 200 ms/1 s podría reintentar la `ValidationException` `[NO PROBADO]` |

V2 es una decisión de herramienta de Tobi (sección "Herramientas"). Sin decisión, el paso sigue con V0.

### 4.3 Logging y trazas

- Por defecto, "`InvokeAsync()` uses lightweight tracking without emitting detailed log messages"; `opts.InvokeTracing = InvokeTracingMode.Full` emite los logs de un mensaje recibido ([FUENTE: Wolverine, Mediator tutorial](https://wolverinefx.net/tutorials/mediator.html)). Sin paquete.
- El repo ya suma la fuente de actividades `Wolverine` a las trazas (`architecture.md`, "Bus"); `InvokeInlineAsync` abre un span de ejecución por invocación ([FUENTE: código, `Executor.cs:144`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Runtime/Handlers/Executor.cs#L144)). El `traceId` del 500 sigue uniendo la respuesta con el log `[INFERENCIA]`.
- Middleware propio por convención (`Before`/`Finally`), aplicado con `options.Policies.AddMiddleware(tipo, filtro)` ([FUENTE: Wolverine, Middleware](https://wolverinefx.net/guide/handlers/middleware.html)); el repo ya lo hace con `IntegrationEnvelopeCheck` `[CÓDIGO MessagingExtension.cs:107]`. No lo propongo en este paso (regla 7).

### 4.4 Transacciones

- **Hoy:** cada slice abre su transacción. `CreateTask` usa `CrossContextTransaction.Begin()` después de la llamada a Projects `[CÓDIGO CreateTask.cs:49-58]`, y el filtro de idempotencia envuelve el endpoint en otro `TransactionScope` (`conventions.md`, "Persistencia").
- **`AutoApplyTransactions` con EF:** envuelve en transacción "any message handler that has a dependency on any type of `DbContext`", también si el `DbContext` es dependencia de un servicio inyectado; corre en modo `Eager` (`BeginTransactionAsync` antes del handler); con dos `DbContext` falla al arrancar ("Wolverine will not guess"); `[NonTransactional]` lo apaga por handler ([FUENTE: Wolverine, EF Core Transactional Middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html)).
- **Choque en el monolito:** `AutoApplyTransactions` ya está prendido para todos los handlers `[CÓDIGO SagaStorage.cs:22]`. Un handler de Projects, Users o Auth que dependa de su contexto recibiría una transacción de EF y el enganche al outbox de Wolverine (`EfCoreEnvelopeTransaction`, como en `AccountClosureGraceExpiredHandler503767472.cs:41-48`), además de su `TransactionScope` propio. Si depende de dos contextos (por ejemplo vía `IUsersModule`), el monolito no arranca `[INFERENCIA]`. `[NonTransactional]` está en `Wolverine.Attributes`: la slice nombraría Wolverine.
- **En Tasks no hay choque:** no hay integración de EF con Wolverine `[CÓDIGO Hosts/Tasks/Program.cs:17]`. Es la razón principal para empezar por Tasks (sección 8.5).
- Adoptar las transacciones de Wolverine en Tasks pide `UseEntityFrameworkCoreTransactions` y persistencia en PostgreSQL de Wolverine en ese proceso: los paquetes ya están, pero cambia la decisión del paso 25 (outbox casero; Wolverine solo transporte, salvo la saga) y la del 27. Es otro paso.

### 4.5 El outbox casero del paso 24

No cambia. Las entidades anotan sus eventos y `OutboxInterceptor` los guarda en el `SaveChanges` del handler (`conventions.md`, "Eventos y outbox"). El handler corre en otro scope, pero el `DbContext` sale del mismo contenedor con sus interceptores `[INFERENCIA]`; el test `Create_WithKey_WhenItFailsAfterTheTaskWasSaved_LeavesNeitherKeyNorTask` agrega un interceptor por `ConfigureDbContext` y lo comprueba `[CÓDIGO backend/Features/Tasks/CreateTask/CreateTaskTests.cs:222-237]`.

### 4.6 Permisos por request

- **Choque:** `RemoteSession` guarda los permisos en el `TaskAccess` del scope del request (`http.RequestServices.GetRequiredService<TaskAccess>().Grant(permissions)`) `[CÓDIGO backend/Features/Tasks/Access/RemoteSession.cs:18]`, y `CreateTask` los lee `[CÓDIGO CreateTask.cs:36-37]`. El handler recibe un `TaskAccess` nuevo, sin permisos (sección 1.3): `POST /tasks` respondería 403 `[INFERENCIA]`.
- **Opciones:**
  - (P1, recomendada) El comando lleva al que llama: `Caller(string Id, IReadOnlySet<string> Permissions)`, armado por el endpoint desde `ClaimsPrincipal` y `TaskAccess`. El handler no depende de HTTP y se puede llamar desde un test o una cola. `CanAccess`/`HasPermission` pasan a `Caller`.
  - (P2) Un `TaskAccess` registrado con una fábrica que lee `IHttpContextAccessor`. Esconde la dependencia de HTTP adentro del handler y es una registración opaca (service location).
  - (P3) El chequeo de permisos queda en el endpoint. Rompe "La slice hace el caso de uso completo" `[CÓDIGO ADR-0004:45]`.

## 5. Estilo de endpoint

| | (H0) Minimal API + `IMessageBus.InvokeAsync<T>` | (H1) Wolverine.HTTP (`WolverineFx.Http`) |
|---|---|---|
| Paquete | ninguno | **nuevo** ([FUENTE: Wolverine HTTP](https://wolverinefx.net/guide/http/)) |
| Forma | el `*Endpoint.cs` de hoy, con `IMessageBus` en vez de la slice: `app.MapPost(..., (CreateItemCommand cmd, IMessageBus bus) => bus.InvokeAsync(cmd))` ([FUENTE: Mediator tutorial](https://wolverinefx.net/tutorials/mediator.html)) | el método del endpoint **es** el handler (`[WolverinePost]`), o `MapPostToWolverine<TReq, TRes>` que "bypasses some of the performance overhead of 'classic mediator' usage" ([FUENTE: Using as Mediator](https://wolverinefx.net/guide/http/mediator.html)) |
| Lo del repo | `WithIdempotency`, `ApiResults`, versionado (DUNE0012), antiforgery y DUNE0002 siguen igual | hay que rehacer filtros, versionado y el mapeo de errores en el modelo de Wolverine; `MapWolverineEndpoints` y `AddWolverineHttp()` obligatorio ([FUENTE: Wolverine HTTP](https://wolverinefx.net/guide/http/)) |
| Scope de DI | aislado (4.6) | puede usar `HttpContext.RequestServices` ([FUENTE](https://wolverinefx.net/guide/http/#using-the-httpcontext-requestservices)) |
| Qué dice el equipo de Wolverine | "a reasonable first step when migrating" | "For new code though, we believe you can build simpler code with 'just' a Wolverine.HTTP endpoint" ([FUENTE: Best Practices](https://wolverinefx.net/introduction/best-practices.html)) |
| Concepto del paso | se ve el mediator: dos piezas, endpoint y handler | desaparece el mediator: es otro concepto (endpoint = handler) |

H1 es una decisión de herramienta de Tobi.

## 6. Tests

- **Lo que queda igual.** Los tests de `CreateTaskTests.cs` van por HTTP con `ApiFixture` y miden contrato, idempotencia, métricas y base `[CÓDIGO CreateTaskTests.cs:12-288]`. El contrato no cambia, así que siguen siendo la red del paso (regla 3 no aplica: no cambia un contrato HTTP).
- **Lo que se suma:**
  - Un test por handler que lo llama directo, sin HTTP, con un `Caller` armado a mano (P1). Es la ventaja que Wolverine promete: handlers "easy to unit test" ([FUENTE: A-Frame](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/)). Con `TasksDbContext` real (Testcontainers), es un test de integración chico `[INFERENCIA]`.
  - Un test que fija que un fallo no se reintenta (4.1) y uno que fija que ningún camino devuelve un éxito vacío (3.2).
  - Un test que resuelve el `IMessageBus` del host y verifica que cada comando registrado tiene handler: sin handler local, Wolverine intenta rutear y falla en el primer request `[INFERENCIA]`.
- **Sesiones rastreadas.** `host.InvokeMessageAndWaitAsync(msg)` espera a que termine todo lo que el mensaje dispara y junta las excepciones ([FUENTE: Wolverine, Testing](https://wolverinefx.net/guide/testing.html#integration-testing-with-tracked-sessions)). Está en `Wolverine.Tracking`, en el núcleo ([FUENTE: código, `WolverineHostMessageTrackingExtensions.cs`](https://github.com/JasperFx/wolverine/blob/V6.64.2/src/Wolverine/Tracking/WolverineHostMessageTrackingExtensions.cs)). Con handlers que no publican nada, no hace falta.
- **Varios hosts en un proceso.** El ensamblado de aplicación se cachea en un estático; "whichever host runs first can pin handler discovery for every later host that doesn't set one explicitly" ([FUENTE: Wolverine, Testing](https://wolverinefx.net/guide/testing.html)). `ApiPair` levanta los dos procesos juntos `[CÓDIGO backend/Tests/Support/ApiPair.cs:89]`: con `ApplicationAssembly` fijado en los dos (2.4) no aplica.
- Los tests quedan fuera de DUNE0025, como hoy (`AccountClosureGracePeriodTests.EndGracePeriodAsync` ya usa `InvokeAsync`, `docs/decisions.tsv` paso 28).

## 7. Guía profesional y trampas conocidas

- **Mediator detrás de HTTP: soportado, no preferido.** "Using Wolverine as a 'Mediator' in MVC controllers, Minimal API functions, or maybe Hot Chocolate mutations is an exception case that we fully support" ([FUENTE: Wolverine, Modular Monolith](https://wolverinefx.net/tutorials/modular-monolith.html)). Jeremy recomienda para HTTP "just using the Wolverine.HTTP mechanism" ([FUENTE: Wolverine for MediatR Users](https://jeremydmiller.com/2025/01/28/wolverine-for-mediatr-users/)); su serie empieza con el mediator en un controller y después lo saca ([FUENTE: Wolverine as Mediator](https://jeremydmiller.com/2023/12/03/building-a-critter-stack-application-wolverine-as-mediator/)).
- **Nada de `InvokeAsync` anidado.** "we pretty well never recommend calling `IMessageBus.InvokeAsync()` inline in any message handler to another message handler" ([FUENTE: Modular Monolith](https://wolverinefx.net/tutorials/modular-monolith.html)); "it hides a nested transaction, hides the database round trips, and splits one unit of work into two" ([FUENTE: Best Practices](https://wolverinefx.net/introduction/best-practices.html)); en sagas, "You will be acting on old or missing data" ([FUENTE: Sagas](https://wolverinefx.net/guide/durability/sagas.html)). DUNE0025 lo hace ejecutable: `Invoke*` solo en un `*Endpoint.cs`.
- **Efectos visibles desde el handler raíz.** "publish outgoing messages only from the root handler method" (misma fuente, "Make All Side Effects Apparent from the Root Handler"). Coincide con la regla del repo: una slice no escribe filas del outbox, la entidad anota sus eventos.
- **A-Frame y funciones puras.** "the business logic function which in Wolverine world becomes a pure function" ([FUENTE: A-Frame](https://jeremydmiller.com/2023/07/19/a-frame-architecture-with-wolverine/)); los handlers compuestos (`Load` + `Handle`) son la forma de Wolverine ([FUENTE: Message Handlers](https://wolverinefx.net/guide/handlers/)). En este paso no lo propongo: `CreateTask` hace una llamada remota y abre su transacción en un orden que importa (lápida después de Projects, `conventions.md` "Persistencia"), y partirlo en `Load`/`Handle` es otro cambio. Va a pendientes.
- **Request/reply entre procesos.** `InvokeAsync<T>` a un endpoint remoto existe, pero "reintroduces exactly the temporal coupling that asynchronous messaging was supposed to remove" ([FUENTE: Best Practices, Prefer Purely Asynchronous Processing](https://wolverinefx.net/introduction/best-practices.html)). El repo sigue con HTTP y la resiliencia del paso 29 para Tasks → Projects.
- **Timeouts.** El de 5 s es solo remoto (1.3). En línea, el límite es el del request (YARP 15 s, `plan.md` paso 29).
- **Depurar el codegen.** `opts.DescribeHandlerMatch(typeof(X))` explica por qué un tipo es o no handler ([FUENTE: Discovery, Troubleshooting](https://wolverinefx.net/guide/handlers/discovery.html#troubleshooting-handler-discovery)); el código generado versionado se lee como cualquier archivo; "you may need to delete the existing source code when you change handler signatures" ([FUENTE: Code Generation](https://wolverinefx.net/guide/codegen.html)), y `check-codegen.mjs` ya lo detecta.
- **`MediatorOnly` no aplica.** "completely disables all asynchronous messaging" ([FUENTE: Mediator tutorial](https://wolverinefx.net/tutorials/mediator.html)); el repo usa RabbitMQ.

## 8. Propuesta para este repo

### 8.1 Cómo queda una slice (forma A, recomendada: handler de Wolverine con codegen)

```
Features/Tasks/CreateTask/
  CreateTaskRequest.cs           // cuerpo HTTP, internal (igual)
  CreateTaskRequestValidator.cs  // valida el request (igual)
  CreateTaskCommand.cs           // el mensaje: internal sealed record CreateTaskCommand(Caller Caller, string Title, Guid? ProjectId)
  CreateTask.cs                  // internal static class CreateTask, con un único anidado público: Handler
  CreateTaskEndpoint.cs          // arma el comando y llama bus.InvokeAsync<ErrorOr<TaskResponse>>(command, ct)
  CreateTaskTests.cs             // HTTP (igual) + el handler llamado directo
```

- El handler conserva constructor con dependencias (`TasksDbContext`, `TimeProvider`, `TaskMetrics`, `ProjectsClient`) y un `Handle(CreateTaskCommand, CancellationToken)` que devuelve `ErrorOr<TaskResponse>`: el cuerpo de `ExecuteAsync` de hoy, sin `TaskAccess` (usa `command.Caller`). Mantener la inyección por constructor deja funcionando a DUNE0024, que busca clientes remotos en `InstanceConstructors` `[CÓDIGO dune/Dune.Analyzers/RemoteReadAnalyzer.cs:109]` `[INFERENCIA]`.
- `TasksFeature` cambia `services.AddScoped<CreateTask.CreateTask>()` `[CÓDIGO backend/Features/Tasks/TasksFeature.cs:50]` por `services.AddMediatorHandler<CreateTask.Handler>()`.
- El archivo del handler no nombra Wolverine: el método se llama `Handle` y el registro es del repo.
- El anidado público necesita una excepción escrita en `conventions.md` (un tipo, un archivo; anidados privados) y en DUNE0010 si lo cuenta `[NO PROBADO]` si DUNE0010 mira anidados.

### 8.2 Forma B (plan B): adaptador sin codegen

Un `MessageHandler<TCommand>` propio en `Infrastructure/Messaging` que abre un scope, resuelve una interfaz genérica del repo (`ISliceHandler<TCommand, TResult>`) y pone el resultado como respuesta: la misma forma que `IntegrationEventHandler<T>` del paso 25 `[CÓDIGO backend/Infrastructure/Messaging/IntegrationEventHandler.cs:8-31]`. Gana: slices `internal`, sin codegen en Tasks, sin `ServiceLocationPolicy`. Pierde: no es un handler de Wolverine (sin inyección por método, sin handlers compuestos, sin el middleware que se teje en el código generado `[INFERENCIA]`), y enseña menos del concepto. Poner la respuesta en el sobre desde un `MessageHandler` propio `[NO PROBADO]`.

### 8.3 ADR-0004 punto 9 y DUNE0025

- **Punto 9, texto propuesto:** "**Mediator y bus: Wolverine.** Desde el paso 29.1, las escrituras HTTP de una feature son handlers de Wolverine y su endpoint les despacha con `IMessageBus.InvokeAsync<T>` (modo mediator). El handler es la slice: hace el caso de uso completo y devuelve `ErrorOr<T>`. El bus sigue llevando eventos de integración y comandos entre procesos (paso 25) y guarda la saga orquestada (paso 27). Un handler nunca invoca a otro."
- **Entrada en "Cambios":** fecha, "decide Tobi" el paso, y el motivo de aprendizaje (`AGENTS.md`, "Un patrón del plan se aplica aunque `Tasks` no lo necesite").
- **"Alternativas descartadas", MediatR:** sigue descartado por la licencia; el argumento de "indirección invisible" ahora vale para Wolverine también y se acepta a propósito.
- **DUNE0025, regla nueva:**
  - Un nombre de Wolverine o JasperFx fuera de `Infrastructure/Messaging` es error, salvo en el archivo de un orquestador (igual que hoy) y **en un `*Endpoint.cs` de una slice, donde solo puede nombrar `IMessageBus`**.
  - Una llamada `Invoke*` sobre un tipo de Wolverine es error en todo el producto **salvo `IMessageBus.InvokeAsync<T>` en un `*Endpoint.cs`** (hoy la regla mira el nombre del método y el namespace `[CÓDIGO dune/Dune.Analyzers/BusBoundaryAnalyzer.cs:67-75]`).
  - `SendAsync`/`PublishAsync` siguen prohibidos en las features: se publica con `IntegrationEventPublisher`.
- **Gate de bus de `check-structure.mjs`:** acepta `using Wolverine;` en un `*Endpoint.cs` de `backend/Features/` `[CÓDIGO dune/check-structure.mjs:241-245]`. Sin paquete nuevo, la lista de paquetes no cambia.
- Cada cambio, con su fixture válida y su violación deliberada (regla 9), y una fila en `docs/decisions.tsv`.

### 8.4 Regla nueva: DUNE0028 "Endpoint de escritura que no despacha por el mediator"

En una feature que adoptó el mediator (hoy: Tasks), el `*Endpoint.cs` de una slice que mapea `POST`, `PUT`, `PATCH` o `DELETE`:
1. llama a `IMessageBus.InvokeAsync<T>` (la forma genérica) exactamente una vez;
2. el tipo del mensaje está declarado en la carpeta de la misma slice;
3. no recibe por parámetro ni construye un tipo con método `Handle` de su feature (no llama al handler directo).

- **Fixture válida:** `Features/Tasks/CreateTask/CreateTaskEndpoint.cs` con `bus.InvokeAsync<ErrorOr<TaskResponse>>(new CreateTaskCommand(...), ct)`.
- **Violaciones deliberadas:** (a) el endpoint recibe `CreateTask.Handler handler` y llama `handler.Handle(...)`; (b) despacha `UpdateTaskCommand` desde `CreateTaskEndpoint.cs`; (c) usa `InvokeAsync(command)` sin `<T>`.
- Cómo se decide qué feature adoptó el mediator: una lista en el analyzer o, mejor, "la feature registra al menos un `AddMediatorHandler`" `[INFERENCIA]`. Dejarlo en una lista es más simple y se borra cuando migren todas.
- `[NO PROBADO]` si DUNE0003/DUNE0004 necesitan cambios: un endpoint que despacha un comando de otra feature ya nombraría un tipo interior de otra feature, y DUNE0004 lo frena.

### 8.5 Alcance mínimo y orden de commits

**Recomiendo migrar solo las tres escrituras HTTP de Tasks** (`CreateTask`, `UpdateTask`, `DeleteTask`):
- Un proceso: Tasks no tiene `AutoApplyTransactions` ni integración de EF con Wolverine, así que el choque de 4.4 no aparece.
- Una feature: DUNE0028 nace con una lista de una feature.
- Tests de contrato ya escritos para las tres.
- Las lecturas quedan como están (CQRS liviano: escrituras por el mediator, lecturas directas), y las slices que llama el bus (`HoldOwnerTasks`, `ReleaseOwnerTasks`, `DeleteOwnedTasks`) siguen con `IIntegrationEventHandler<T>`: van a pendientes.
- Migrar todo de una vez pegaría con `AutoApplyTransactions` en tres features del monolito y con varias slices de dos contextos (`RegisterUser`), y sería más de un cambio (regla 1).

Orden:
1. **Spike en rojo:** un test que llama `POST /tasks` con el handler anidado (2.2) y el `ErrorOr` como respuesta (3.1), con `codegen write` en Tasks. Si falla el anidado, forma B (8.2) con una fila en `docs/decisions.tsv`.
2. Infraestructura: `AddMediatorHandler`, `IncludeType`, `ServiceLocationPolicy` en los dos procesos, ensamblado de aplicación de Tasks, `codegen` en Tasks y `check-codegen.mjs` con los dos proyectos, y las reglas de error aisladas (4.1) con su test.
3. `CreateTask` (con `Caller`, 4.6), después `UpdateTask` y `DeleteTask`.
4. Dune: DUNE0025 reescrita, gate de bus y DUNE0028, cada una con fixture válida y violación.
5. Docs: ADR-0004, `architecture.md` ("No hay mediator…" y "Bus"), `conventions.md` (regla de bus y anidados), `informe-deep-mediators.md` (nota de que su conclusión cambió), `decisions.tsv`, `progreso.md`, `plan.md` (el paso 29.1).

## Choques con los docs

1. **ADR-0004 punto 9** dice "Sin mediator" y "Ninguna slice lo usa como mediator (DUNE0025)" `[CÓDIGO docs/adr/0004-vertical-slices-deep-modules.md:28]`, y su cambio del paso 25 dice que `IMessageBus.InvokeAsync` "está prohibido en todo el producto" (línea 36). El paso lo cambia a propósito: lo decidió Tobi.
2. **`architecture.md:38`** ("No hay mediator… Ninguna slice conoce Wolverine") y **`conventions.md:104`** (`IMessageBus.InvokeAsync` es error en todo el producto) cambian con el paso.
3. **`informe-deep-mediators.md:121`** concluye "no adoptar mediator ni bus". Ya quedó viejo con el bus (paso 25); con este paso también con el mediator. Propongo una nota al principio del informe, no reescribirlo.
4. **Guía de Wolverine contra dos prácticas del repo:** prefiere Wolverine.HTTP sobre "Wolverine as MediatR" y pide no envolver respuestas en `Result<T>` ([FUENTE: Best Practices](https://wolverinefx.net/introduction/best-practices.html)). El repo usa minimal API y `ErrorOr` en todas las slices. El paso sigue al repo (regla 10: los docs del repo deciden) y lo anota en `docs/decisions.tsv`.
5. **`conventions.md:12`** pide anidados privados; la forma A necesita un anidado público. Excepción nueva, con su regla.
6. **ADR-0004 "No hay una interface por slice"** `[CÓDIGO ADR-0004:46]` descarta copiar la solución del paso 27 (`IAccountClosureSteps`) para los handlers.
7. **Decisiones del paso 25 y 27 que se respetan:** Wolverine como única librería de bus (fila 353), `TypeLoadMode.Static` con descubrimiento apagado (fila 360), código generado versionado (filas 434 y 447), `RegisterMessageType` solo para handlers caseros (fila 452: los comandos del mediator no viajan por el broker y no necesitan nombre estable `[INFERENCIA]`). **Cambian:** DUNE0025 (filas 359 y 443) y `ServiceLocationPolicy` (fila 449: pasa de solo el monolito a los dos procesos).
8. **Modo autónomo:** `AGENTS.md` lo limita a los pasos 23.1 a 29. El 29.1 queda fuera: vuelven la regla 11 (parar dos veces) y la regla 5 `[INFERENCIA]`.

Ningún choque entre dos docs del repo: no hace falta frenar por la regla 10.

## Decisiones (con mi recomendación)

| # | Decisión | Opciones | Recomiendo | Por qué |
|---|---|---|---|---|
| D1 | Forma del handler | A: handler de Wolverine anidado público en clase `internal`; B: adaptador `MessageHandler<T>` sin codegen | A, con B de plan B | A enseña el modelo real de Wolverine (convenciones, codegen, middleware) y deja todo lo demás `internal` |
| D2 | Alcance | escrituras de Tasks; todas las escrituras; todas las slices | escrituras de Tasks | sin `AutoApplyTransactions`, un proceso, regla 1 |
| D3 | Permisos en el handler | P1 `Caller` en el comando; P2 `IHttpContextAccessor`; P3 chequeo en el endpoint | P1 | el handler no depende de HTTP y la slice sigue completa |
| D4 | Reglas de error | E1 regla por cadena que corta; E2 reglas del bus por cadena | E2 | las reglas del paso 25 son para colas |
| D5 | Validación sin paquete | V0 inline; V1 `Validate()` | V0 | V1 devuelve un éxito vacío con `ErrorOr` |
| D6 | `ErrorOr` | sin registrar; `UseResultType` | sin registrar | conserva los `Error` tipados del catálogo |
| D7 | Tests de handler | directo con base real; tracked session | directo | los handlers no publican nada |
| D8 | Endpoint nombra Wolverine | `IMessageBus` directo; envoltorio propio en `Infrastructure` | `IMessageBus` directo | un envoltorio de pasada es un módulo superficial (Ousterhout, `informe-deep-mediators.md`) y esconde el concepto del paso |

## Herramientas (regla 5)

Dos decisiones abiertas. **No elijo:** las decide Tobi. El paso se puede hacer sin ninguna (opción 0: minimal API, validación inline, sin paquetes nuevos).

### T1. Endpoints: `WolverineFx.Http`

- **Gana:** lo que recomienda el equipo de Wolverine para código nuevo ([FUENTE: Best Practices](https://wolverinefx.net/introduction/best-practices.html)); endpoint = handler, una pieza menos; `Validate()` con `ProblemDetails`, `[Entity]` para cargar y 404 automáticos; puede compartir el scope del request ([FUENTE: Wolverine HTTP](https://wolverinefx.net/guide/http/#using-the-httpcontext-requestservices)), lo que resolvería 4.6 de otra forma; `MapPostToWolverine` como paso intermedio ([FUENTE: Using as Mediator](https://wolverinefx.net/guide/http/mediator.html)).
- **Pierde:** con endpoint = handler ya no hay mediator, que es el concepto del paso; hay que rehacer `WithIdempotency`, `ApiResults`, el versionado (DUNE0012), el antiforgery y varias reglas de Dune que miran `Map*` de ASP.NET Core; un paquete más en el gate de bus y codegen para cada endpoint.
- Licencia MIT, como el resto de Wolverine ([LICENSE](https://github.com/JasperFx/wolverine/blob/V6.64.2/LICENSE)).

### T2. Validación: `WolverineFx.FluentValidation` (o `WolverineFx.Http.FluentValidation` con T1)

- **Gana:** la validación sale del handler y corre como middleware generado; el repo ya usa FluentValidation (`FluentValidation.DependencyInjectionExtensions` 12.1.1, `Platform.csproj:40`).
- **Pierde:** el fallo es una `ValidationException` ([FUENTE: Fluent Validation Middleware](https://wolverinefx.net/guide/handlers/fluent-validation.html)), no un `ErrorOr`: hay que traducirla al catálogo de errores en el endpoint o en un handler de excepciones; descubre validadores por escaneo salvo `RegistrationBehavior.ExplicitRegistration`; su regla de descarte convive con las reglas globales del paso 25 (`[NO PROBADO]` el orden).
- Existe también `WolverineFx.DataAnnotationsValidation` ([FUENTE](https://wolverinefx.net/guide/handlers/dataannotations-validation.html)); el repo no usa DataAnnotations.

### Qué hace la industria

MediatR (`IMediator.Send` desde el controller) es el patrón más común en .NET y Wolverine lo reemplaza uno a uno con `InvokeAsync` ([FUENTE: Migrating to Wolverine, From MediatR](https://wolverinefx.net/guide/migrating-to-wolverine.html#from-mediatr)). El equipo de Wolverine empuja a Wolverine.HTTP para código nuevo (misma fuente que T1).

## Pendientes para `progreso.md`

- Migrar las lecturas de Tasks y las escrituras del monolito, con una decisión sobre `AutoApplyTransactions` y `[NonTransactional]` (4.4).
- Handlers compuestos (`Load`/`Validate`/`Handle`) y funciones puras (A-Frame) en `CreateTask` (sección 7).
- Las slices que llama el bus (`HoldOwnerTasks`, `ReleaseOwnerTasks`, `DeleteOwnedTasks`) como handlers de Wolverine en vez de `IIntegrationEventHandler<T>`.
- `InvokeTracing = Full` si los logs del mediator hacen falta (4.3).
- Nota en `informe-deep-mediators.md` sobre su conclusión vieja.

## Verificado / No verificado

**Verificado leyendo** (sin correr nada):
- Firmas y comportamiento de `InvokeAsync`/`InvokeAsync<T>`, respuesta, `default` sin respuesta, cancelación, timeout local ignorado y reintentos en línea: código de Wolverine `V6.64.2` (`IMessageBus.cs`, `MessageBus.cs`, `Executor.cs`, `MessageContext.cs`, `FailureRuleCollection.cs`) y docs.
- `IncludeType` rechaza tipos de primer nivel no públicos y acepta anidados públicos según `Type.IsNotPublic`; métodos públicos solamente: `HandlerDiscovery.cs`.
- `UseResultType` y su efecto: `WolverineOptions.Results.cs`, `ResultUnwrappingActionSource.cs`.
- `ServiceLocationPolicy.NotAllowed` por defecto en 6.x, service location para dependencias `internal`: `codegen.md`, `migration.md`.
- `default(ErrorOr<T>)` es éxito: `ErrorOr.cs` `v2.1.1`.
- Repo: forma de la slice, reglas globales de error, `AutoApplyTransactions` solo en el monolito, Tasks sin codegen ni ensamblado propio, `TaskAccess` del scope del request, DUNE0020 ignora anidados, DUNE0025 y el gate de bus.

**No verificado** (queda para el paso):
- Que el handler público anidado en una clase `internal` arranque, genere código con `codegen write` y compile (2.2). Es el primer commit.
- Que el codegen acepte un handler que devuelve `ErrorOr<T>` (struct genérico) y que `InvokeAsync<ErrorOr<T>>` lo devuelva entero (3.1).
- El mecanismo exacto para aislar las reglas globales de error (4.1) y el comportamiento real con un `TransactionScope` abortado.
- Si DUNE0010 cuenta tipos anidados (8.1) y si `RegisterMessageType` hace falta para comandos que no viajan.
- Cómo resuelve `codegen write` el compilador de Roslyn con `ExtensionDiscovery.ManualOnly`: en el monolito ya funciona (CI, `check-codegen.mjs`), pero no leí por qué; en Tasks puede pedir `opts.UseRuntimeCompilation()` ([FUENTE: Code Generation](https://wolverinefx.net/guide/codegen.html#configuration)).
- El blog de Jeremy Miller lo leí con un resumidor web, no el texto completo; las citas cortas salen de esa lectura.
