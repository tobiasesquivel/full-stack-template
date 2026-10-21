# Comunicación entre features, nombres de contratos y módulos profundos con agentes de IA (investigación)

- Fecha: 2026-10-20.
- Alcance: solo backend. No es un paso del [plan](../plan.md): responde preguntas de Tobi y deja propuestas para pasos futuros.
- Preguntas:
  1. ¿Cómo se hablan dos features cuando una necesita algo de la otra? ¿Se comparte el recurso? ¿Qué pasa con una entidad? En vertical slices sin módulos profundos y con módulos profundos.
  2. ¿Qué convención de nombres usan las fuentes para los contratos entre features?
  3. ¿Los módulos profundos ayudan a programar con agentes de IA? ¿Algún experto desaconseja combinarlos con vertical slices?
- Decisiones que este informe no reabre: [ADR-0004](../../adr/0004-vertical-slices-deep-modules.md) (vertical slices dentro de features que son módulos profundos, un solo proyecto, Dune controla los límites, sin mediator ni bus hasta el paso 25).
- Método: cinco subagentes con WebSearch y WebFetch, uno por tema (vertical slices sin módulos, con módulos, nombres, críticas, IA). El código del repo se leyó directo. Cada afirmación externa lleva su link. Las citas textuales salen del resumen que WebFetch hace de cada página: no se cotejaron letra por letra con el original.
- Etiquetas: **[REPO]** sale del código o de los docs del repo. **[INFERENCIA]** es una lectura propia que ninguna fuente dice tal cual.

## Conclusión

- **Sin módulos profundos, las slices comparten casi todo.** Leen el mismo `DbContext`, usan las mismas entidades y bajan la lógica común a la entidad. Lo que no se hace es que un handler llame a otro.
- **Con módulos profundos, ninguna feature toca el interior de otra.** Le pide las cosas a su contrato, que devuelve datos y nunca entidades, o reacciona a un evento que la otra publica. Cada módulo es dueño de sus datos.
- **Este repo sigue la segunda forma, con una excepción:** todavía comparte un solo `AppDbContext`, y por eso comparte transacción y FKs entre features. El paso 22 lo separa. [REPO]
- **Los nombres de los contratos no siguen una convención.** Las fuentes de monolito modular usan una interfaz por módulo (`I<Módulo>Module`) y datos con nombre de propósito (`OrderSummary`, `OrderShippingInfo`). Ninguna usa `Snapshot` como convención.
- **Para agentes de IA, los módulos profundos tienen apoyo de practicantes con renombre y ninguna crítica en contra.** No hay evidencia empírica directa. Los referentes de vertical slices combinan slices con módulos; las críticas apuntan a capas dentro de las slices, fronteras prematuras y módulos que solo reenvían.

## 1. Cómo se hablan dos features

### 1.1 Vertical slices sin módulos profundos

Cada slice = request + handler + endpoint, con un solo `DbContext` compartido.

- **Regla general:** "minimize coupling between slices, and maximize coupling in a slice" ([Bogard](https://www.jimmybogard.com/vertical-slice-architecture/)).
- **Leer datos de otra slice: consulta directa al `DbContext`.** En ContosoUniversity, el handler de `Instructors/CreateEdit` recibe `SchoolContext db` y lee `db.Courses` sin pasar por la slice de Courses ([código](https://raw.githubusercontent.com/jbogard/ContosoUniversityDotNetCore-Pages/master/ContosoUniversity/Pages/Instructors/CreateEdit.cshtml.cs)). Milan dice lo mismo: "If `CreateOrder` needs customer data, it queries the database directly" ([Milan](https://milanjovanovic.tech/blog/vertical-slice-architecture-where-does-the-shared-logic-live)).
- **Llamar a otro handler: no.** Bogard partió `IMediator` en `ISender` e `IPublisher` para "catch design errors, where [you] should never send requests from anywhere inside a request handler" ([MediatR 9.0](https://www.jimmybogard.com/mediatr-9-0-released/)).
- **Lógica pura compartida: se extrae, pero tarde.** "Reuse policies and calculators; let handlers duplicate" ([Dudycz](https://event-driven.io/en/vertical-slices-and-dependencies/)). "Duplication is cheaper than the wrong abstraction" y regla de tres ([Milan](https://milanjovanovic.tech/blog/vertical-slice-architecture-where-does-the-shared-logic-live)).
- **Entidades: se comparten.** ContosoUniversity tiene `Models/` y `Data/` fuera de las slices ([repo](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages/tree/master/ContosoUniversity)). Milan las pone en `Domain/` ([Milan](https://milanjovanovic.tech/blog/vertical-slice-architecture-where-does-the-shared-logic-live)).
- **Reglas de negocio: se bajan a la entidad.** Se empieza con un transaction script y, cuando aparece un code smell, la lógica pasa a la entidad ([Bogard](https://www.jimmybogard.com/vertical-slice-architecture/), [pushing behavior down](https://jimmybogard.com/domain-driven-refactoring-defactoring-and-pushing-behavior-down)). Así se prueba "without mocks or stubs".
- **Reacciones: eventos de dominio.** La entidad junta `INotification` y `SaveEntitiesAsync` los despacha ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)). Pub/sub no es obligatorio: "Vertical Slices do not require it" ([Comartin](https://codeopinion.com/?p=10468)). Si hace falta fiabilidad, outbox con consumidores idempotentes ([Milan](https://www.milanjovanovic.tech/blog/implementing-the-outbox-pattern), [Dudycz](https://event-driven.io/en/Outbox_Inbox_patterns_and_delivery_guarantees_explained/)).
- **No es "no compartir nada":** "It is about sharing the right things" ([Comartin](https://codeopinion.com/vertical-slices-doesnt-mean-share-nothing/)). Un `Common/` termina siendo "a junk drawer" ([Milan](https://milanjovanovic.tech/blog/vertical-slice-architecture-where-does-the-shared-logic-live)).
- **Desacuerdos:**
  - Dudycz quiere que cada slice declare dependencias angostas que conecta la raíz de composición. Bogard y Milan leen el `DbContext` directo ([Dudycz](https://event-driven.io/en/vertical-slices-and-dependencies/)).
  - Transacción: Bogard despacha los efectos antes del commit, en la misma transacción. Evans y Vernon piden una transacción por agregado. Milan publica después del commit con outbox ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation), [Milan](https://www.milanjovanovic.tech/blog/using-domain-events-to-build-loosely-coupled-systems)).

### 1.2 Vertical slices con módulos profundos (monolito modular)

- **Formas de integrar, de más a menos acoplada:** datos compartidos, llamada directa, mensajería ([Grzybek](https://www.kamilgrzybek.com/blog/posts/modular-monolith-integration-styles)).
- **Llamada directa: una API pública chica.** Se arranca sin nada público y se expone solo lo que otro módulo pide. Los métodos llevan nombre de caso de uso y devuelven DTOs, nunca entidades ([Milan](https://www.milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths)). El módulo dueño implementa la interfaz; los demás ven solo `Contracts` ([Milan](https://milanjovanovic.tech/blog/module-boundaries-bounded-contexts)).
- **Mensajería:**
  - Evento de dominio: dentro del módulo, puede ir en la misma transacción. Evento de integración: hacia otros módulos, solo después de guardar ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)).
  - Los eventos internos no se publican afuera: se traducen a un evento externo resumido ([Dudycz](https://event-driven.io/en/internal_external_events/)).
  - Outbox: el mensaje se guarda en la misma transacción y otro proceso lo envía; el receptor es idempotente ([Richardson](https://microservices.io/patterns/data/transactional-outbox.html)).
- **Datos:** cada módulo tiene su esquema y compartir datos está prohibido ([repo de Grzybek](https://github.com/kgrzybek/modular-monolith-with-ddd)). Un `DbContext` por módulo con `HasDefaultSchema` ([Milan](https://www.milanjovanovic.tech/blog/using-multiple-ef-core-dbcontext-in-single-application)).
- **Entidades: nunca salen del módulo.** Pasar la entidad o el `DbContext` "borra el límite" ([Milan](https://milanjovanovic.tech/blog/modular-monolith-architecture-dotnet)). Si el módulo B necesita "un User" tiene tres opciones ([Milan](https://milanjovanovic.tech/blog/module-boundaries-bounded-contexts), [Comartin](https://codeopinion.com/stop-joining-tables-in-your-modular-monolith/)):
  - guardar solo el Id y su propio modelo del concepto;
  - guardar una copia congelada a propósito (el precio al momento del pedido);
  - pedir el dato a la API del dueño.
  Comartin prefiere rediseñar el límite para que B no necesite ese dato. Milan avisa: si cada vez hacen falta más handlers para sincronizar dos módulos, es "acoplamiento disfrazado" ([Milan](https://www.milanjovanovic.tech/blog/the-modular-monolith-boundary-i-couldnt-take-back)).
- **Shared kernel:** un conjunto chico y explícito, como las abstracciones del bus ([Milan](https://milanjovanovic.tech/blog/module-boundaries-bounded-contexts)). Grzybek lo llama `BuildingBlocks` ([repo](https://github.com/kgrzybek/modular-monolith-with-ddd/tree/master/src/BuildingBlocks)).
- **Transacciones:** sin constraints ni transacciones entre módulos ([Grzybek](https://www.kamilgrzybek.com/blog/posts/modular-monolith-integration-styles)). Si dos cosas deben ser correctas al mismo instante, van en el mismo módulo ([Milan](https://www.milanjovanovic.tech/blog/the-modular-monolith-boundary-i-couldnt-take-back)).
- **Cómo se hacen cumplir:** `internal`, referencias de proyecto y tests de arquitectura con NetArchTest o ArchUnitNET ([Milan](https://milanjovanovic.tech/blog/shift-left-with-architecture-testing-in-dotnet)).
- **Desacuerdo:** el repo de Grzybek permite comunicarse "only asynchronously using Events Bus" ([repo](https://github.com/kgrzybek/modular-monolith-with-ddd)). Su blog y Milan aceptan la llamada directa cuando hace falta consistencia inmediata ([Grzybek](https://www.kamilgrzybek.com/blog/posts/modular-monolith-integration-styles), [Milan](https://www.milanjovanovic.tech/blog/modular-monolith-communication-patterns)).
- **Ousterhout aplicado al límite:** la interfaz es "todo lo que otros módulos deben saber", incluidos efectos secundarios y restricciones; la fuga de información es lo opuesto al ocultamiento ([CS190](https://web.stanford.edu/~ouster/cgi-bin/cs190-winter18/lecture.php?topic=modularDesign)). [INFERENCIA] Exponer una entidad, un `DbContext` o un evento interno es fuga; un join SQL entre esquemas es fuga por la puerta de atrás.

### 1.3 Tabla: qué se comparte y cómo

| Qué necesita la feature A de B | Sin módulos profundos | Con módulos profundos | Este repo [REPO] |
|---|---|---|---|
| Leer datos de B | LINQ directo al `DbContext` | Contrato de B que devuelve datos | Contrato (`IUserProfiles.FindAsync`) |
| Que B haga una acción | Duplicar o extraer lógica pura; nunca handler → handler | Método del contrato con nombre de caso de uso | Contrato (`IUserAccounts.DeleteAsync`) |
| Una entidad de B | Se comparte la clase | Nunca: Id, dato de contrato o copia | Id + dato de contrato |
| Una regla de negocio | Se baja a la entidad | Se baja a la entidad, dentro de B | Igual (`UserProfile.IsValidDisplayName`) |
| Reaccionar a algo de B | Evento de dominio | Evento de integración + outbox | Sin eventos (pasos 24 y 25) |
| Tablas y `DbContext` | Uno para todos | Uno por módulo, esquema propio | Uno solo (paso 22) |
| Transacción entre A y B | Gratis | Evitarla; si es imprescindible, quizá son un módulo | Sí, en el registro |
| Tipos base | Carpeta común | Shared kernel chico | `Shared/` sin nombres de feature |

### 1.4 Cómo está el repo hoy [REPO]

- Auth crea el perfil en el registro con `IUserProfiles.CreateAsync` (`Users/Contracts`). La implementación `UserProfiles` es interna de Users y devuelve `UserProfileSnapshot`, no la entidad `UserProfile`.
- Users lee y borra la cuenta con `IUserAccounts` (`Auth/Contracts`) y recibe `UserAccount`.
- Tasks y Users preguntan permisos con `IAccessPolicy` (`Auth/Contracts`).
- `TaskItem.OwnerId` es un `string` sin navegación (`conventions.md`: "Entre features, una entidad guarda solo el Id de la otra").
- DUNE0004 corta la compilación si una feature usa algo fuera del `Contracts` de otra (`dune/Dune.Analyzers/CrossFeatureAnalyzer.cs`).
- Diferencias con un módulo profundo puro:
  1. Un solo `AppDbContext`. `RegisterUser` abre la transacción y `UserProfiles.CreateAsync` guarda dentro de ella (`RegisterUser.cs:45-63`).
  2. La cascada de borrado la hace la base, con las FKs de `AppDbContext.ConfigureCrossFeatureRelationships`.
  3. `AppIdentityUser`, una entidad, está en `Auth/Contracts` porque `AppDbContext` hereda de `IdentityDbContext<AppIdentityUser>` (`architecture.md`, diferencia del paso 22).
  4. No hay eventos.

## 2. Nombres de los contratos

### 2.1 Inventario de `Contracts/` [REPO]

Quién usa cada tipo, contado con `grep` fuera de tests y migraciones:

| Tipo | Clase de tipo | Lo usan |
|---|---|---|
| `IUserProfiles` | Interfaz (puerta) | Auth, Users |
| `IUserAccounts` | Interfaz (puerta) | Auth, Users |
| `IAccessPolicy` | Interfaz (puerta) | Auth, Tasks, Users |
| `UserProfileSnapshot` | Dato entre features | Auth, Users |
| `UserAccount` | Dato entre features (parece entidad) | Auth, Users |
| `UserResponse` | Response HTTP, también entre features | Auth, Users |
| `TaskResponse`, `TaskListResponse` | Response HTTP | solo Tasks |
| `HealthStatusResponse` | Response HTTP | solo Health |
| `TaskPermissions`, `UserPermissions` | Constantes | Auth + su feature |
| `UserEndpointNames` | Constante | Auth, Users |
| `ClaimsPrincipalExtensions` (`GetCallerId`) | Helper | Auth, Tasks, Users |
| `AppIdentityUser` | Entidad expuesta | Auth, Infrastructure |

Tres formas de nombrar conviven: plural de sustantivo (`IUserProfiles`), rol (`IAccessPolicy`) y dato sin propósito (`UserAccount`, `UserProfileSnapshot`). `Contracts/` mezcla además puertas, datos, responses HTTP que ninguna otra feature usa, constantes y un helper. Lo permite `conventions.md` ("lo que ven otras features y las responses HTTP que comparten varias slices").

### 2.2 Qué usan las fuentes

| Tipo de contrato | Convención | Fuente |
|---|---|---|
| Regla base de interfaces | Prefijo `I`; adjetivo o, a veces, sustantivo | [Microsoft, naming](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces) |
| Puerta del módulo | `IMeetingsModule`, `IUserAccessModule` con `ExecuteCommandAsync` y `ExecuteQueryAsync` | [Grzybek](https://github.com/kgrzybek/modular-monolith-with-ddd) |
| Puerta del módulo, por caso de uso | `IOrdersModule.GetOrderForShippingAsync`; "Design the API around use cases, not data" | [Milan](https://milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths) |
| Proyecto de contratos | `<Módulo>.Contracts` con `ICatalogModule` | [Milan](https://milanjovanovic.tech/blog/build-modular-monolith-dotnet-step-by-step) |
| Servicio de aplicación | Sufijo `AppService` (`IProductAppService`) | [ABP](https://abp.io/docs/latest/framework/architecture/best-practices/application-services) |
| Interfaz de lecturas | `IOrderQueries` | [eShop](https://github.com/dotnet/eShop/blob/main/src/Ordering.API/Application/Queries/IOrderQueries.cs) |
| Dato de detalle | `MeetingDetailsDto` | [Grzybek](https://github.com/kgrzybek/modular-monolith-with-ddd) |
| Dato de resumen | `OrderSummary` | [eShop](https://github.com/dotnet/eShop/blob/main/src/Ordering.API/Application/Queries/IOrderQueries.cs), [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads) |
| Dato para un consumidor | `OrderShippingInfo`: "Public DTO with only what shipping needs" | [Milan](https://milanjovanovic.tech/blog/internal-vs-public-apis-in-modular-monoliths) |
| Response | `ProductResponse` (`sealed record`) | [Milan](https://milanjovanovic.tech/blog/build-modular-monolith-dotnet-step-by-step) |
| Entrada | Una por método: "Do not share input DTOs between application service methods" | [ABP](https://abp.io/docs/latest/framework/architecture/best-practices/application-services) |
| Evento de integración | Pasado + `IntegrationEvent` (`MemberCreatedIntegrationEvent`, `OrderStockConfirmedIntegrationEvent`) | [Grzybek](https://github.com/kgrzybek/modular-monolith-with-ddd), [eShop](https://github.com/dotnet/eShop), [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation) |
| Interfaz por rol | Un rol = una interacción concreta; lo opuesto es la "header interface" | [Fowler, RoleInterface](https://martinfowler.com/bliki/RoleInterface.html) |

- **Desacuerdos:**
  - `Dto` sí (Grzybek, ABP) o nombre por propósito sin `Dto` (Milan, eShop). Ninguna fuente prohíbe `Dto`.
  - Puerta genérica con dos métodos (Grzybek) o métodos por caso de uso (Milan).
  - Fowler: los DTO dentro del mismo proceso "are actually harmful" salvo que el modelo de pantalla sea muy distinto o haya partes aisladas ([LocalDTO](https://martinfowler.com/bliki/LocalDTO.html)). Milan y Grzybek los usan entre módulos para no filtrar el dominio.
  - Microsoft prefiere adjetivos en interfaces; las fuentes de monolito modular usan sustantivos.

## 3. Módulos profundos y agentes de IA

### 3.1 Qué dicen a favor

- **Matt Pocock** (opinión, [AI Hero](https://www.aihero.dev/how-to-make-codebases-ai-agents-love)): la IA "is a new starter with no memory"; "Your codebase, way more than your prompt or your AGENTS.md file, is the biggest influence on AI's output". Propone "progressive disclosure of complexity" y "grey box modules": "You own the interface. AI owns the implementation. Tests keep it honest."
- **Skills de Pocock** ([codebase-design](https://raw.githubusercontent.com/mattpocock/skills/main/skills/engineering/codebase-design/SKILL.md), [improve-codebase-architecture](https://raw.githubusercontent.com/mattpocock/skills/main/skills/engineering/improve-codebase-architecture/SKILL.md)): "The interface is the test surface"; "The aim is testability and AI-navigability". La skill `codebase-design` está instalada en este entorno.
- **Thoughtworks Technology Radar** (abril 2025, [entrada](https://www.thoughtworks.com/en-gb/radar/techniques/ai-friendly-code-design)): "modularity and abstractions keep AI's context manageable by limiting necessary changes"; "the best AI-friendly patterns align with established best practices".
- **Anthropic** ([context engineering](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents)): el contexto es "a finite resource with diminishing marginal returns" y hay "context rot". No nombra los módulos profundos.
- **Birgitta Böckeler** ([martinfowler.com](https://martinfowler.com/articles/exploring-gen-ai/context-engineering-coding-agents.html)): "An agent's effectiveness goes down when it gets too much context."
- **Kent Beck** ([Augmented Coding & Design](https://newsletter.kentbeck.com/p/augmented-coding-and-design)): el agente "lack[s] taste"; "Manually improving the design reduces complexity, for me & the genie".
- **Jimmy Bogard** ([webinar](https://www.jimmybogard.com/vertical-slice-architecture-webinar/)): las slices acortan los ciclos "by reducing side-effects and coupling". No nombra los módulos profundos.
- **John Ousterhout** ([Pragmatic Engineer](https://newsletter.pragmaticengineer.com/p/the-philosophy-of-software-design)): según el editor, el buen diseño importa más con IA y los agentes se parecen a "tactical tornadoes". Son paráfrasis del editor, no citas.

### 3.2 Advertencias

- Pocock pide "apply taste at the boundaries" ([AI Hero](https://www.aihero.dev/how-to-make-codebases-ai-agents-love)).
- La skill `codebase-design` advierte contra los pasamanos: "If complexity vanishes, it was a pass-through"; "One adapter means a hypothetical seam" ([SKILL.md](https://raw.githubusercontent.com/mattpocock/skills/main/skills/engineering/codebase-design/SKILL.md)).
- Fuentes débiles, sin autor experto: un falso módulo profundo esconde lógica sin relación y confunde al agente ([Howardism, contenido generado por IA](https://www.howardism.dev/articles/deep-modules-for-agents)); los agentes rinden mejor con código explícito y local ([HackerNoon](https://hackernoon.com/ai-agents-reward-boring-code-stop-being-clever), anécdota).
- Ningún experto reconocido dice que los módulos profundos perjudican a los agentes.

### 3.3 Vertical slices + módulos: ¿chocan?

- **Milan:** el módulo es la decisión macro y la slice la micro: "each module independently picks its micro architecture, which is where vertical slices fit"; con esa frontera "You don't need layers to protect you" ([Milan](https://www.milanjovanovic.tech/blog/where-vertical-slices-fit-inside-the-modular-monolith-architecture)).
- **Dudycz:** "A module is a logical grouping of slices"; "Rules that block a module from reaching another module are useful" ([Dudycz](https://event-driven.io/en/vertical-slices-and-dependencies/)).
- **Bogard:** charla "Modularizing the Monolith" sobre fronteras de módulos y extracción ([Sessionize](https://sessionize.com/s/jimmy-bogard/modularizing-the-monolith/87538)). [INFERENCIA] Que combine las dos ideas sale de la charla y su curso; el artículo original de VSA no habla de módulos.
- **Vertical slices sola no hace cumplir límites:** en un solo proyecto "doesn't stop a handler from referencing anything else" ([Milan](https://milanjovanovic.tech/blog/when-to-choose-vertical-slice-architecture)). [INFERENCIA] Para un agente, el contrato del módulo más DUNE0004 es el límite que la slice sola no da.

### 3.4 Críticas reales (ninguna contra combinar)

- **Capas obligatorias en cada slice:** Bogard critica "Controller MUST talk to a Service that MUST use a Repository" ([Bogard](https://www.jimmybogard.com/vertical-slice-architecture/)).
- **Fronteras prematuras:** "even experienced architects working in familiar domains have great difficulty getting boundaries right at the beginning" ([Fowler, MonolithFirst](https://martinfowler.com/bliki/MonolithFirst.html)); "Getting module boundaries wrong is expensive to fix" ([Milan](https://www.milanjovanovic.tech/blog/where-vertical-slices-fit-inside-the-modular-monolith-architecture)); no todo es un bounded context ([Dudycz](https://event-driven.io/en/vertical-slices-and-dependencies/)).
- **Partir de más:** Ousterhout y Robert C. Martin coinciden en que "it is possible to over-decompose" ([aposd-vs-clean-code](https://github.com/johnousterhout/aposd-vs-clean-code)). El debate es sobre métodos y clases, no sobre features.
- **Desacuerdo interno de Milan:** en [un artículo](https://milanjovanovic.tech/blog/when-to-choose-vertical-slice-architecture) recomienda Clean Architecture para un dominio rico; en [otro](https://www.milanjovanovic.tech/blog/where-vertical-slices-fit-inside-the-modular-monolith-architecture) llama "misconception" a esa idea.

### 3.5 Evidencia empírica

- No hay estudios que comparen la profundidad de los módulos con el éxito de un agente.
- Indirecta: [Borg et al., arXiv 2601.02200](https://www.arxiv.org/pdf/2601.02200) (CodeScene y Universidad de Lund): el código con mejor CodeHealth conserva mejor su comportamiento cuando un LLM lo refactoriza. CodeScene vende esa métrica.
- Sugerente: [CodeTeam, arXiv 2606.22082](https://arxiv.org/html/2606.22082): sin control de interfaces, los sistemas se degradan más al crecer el acoplamiento. Mide generación de repos, no módulos profundos.

## 4. Propuestas para este repo

Ninguna se ejecuta en este informe. Cada una es un paso futuro (reglas 1, 7 y 8 de `AGENTS.md`).

**Decisión de Tobi (2026-10-20):** opción A de la propuesta 1; la propuesta 2 va a `conventions.md`; la 3 entra en el mismo paso. Las tres forman el paso 6.3 del plan, después del 6.2 (`docs/decisions.tsv`).

1. **Convención de nombres para `Contracts/`.** ⏸ **DECISIÓN DE TOBI** entre dos opciones:
   - **A. Una puerta por feature, `I<Feature>Module`** (Grzybek, Milan), con métodos por caso de uso. Gana una sola puerta visible por feature; pierde interfaces chicas por rol (Tasks vería toda la puerta de Auth).
   - **B. Interfaces por rol con sufijo fijo** (Fowler, RoleInterface). Gana interfaces chicas; pierde la señal en el nombre de "esto es la puerta de otra feature".
   - En las dos: datos entre features con `<Cosa><Propósito>` y un sufijo de lista cerrada (`Summary`, `Details`, `Info`); `Response` solo para HTTP; eventos futuros en pasado + `IntegrationEvent`. Renombrar `UserAccount`, que parece entidad.
   - Recomendación: **A**, porque es la forma de las dos fuentes de monolito modular más citadas y un agente encuentra la puerta de cada feature buscando un solo nombre. [INFERENCIA]
   - Regla 4: la convención se hace ejecutable en Dune (por ejemplo, una regla sobre los nombres de tipos públicos en `Contracts/`), con fixture válida y violación deliberada.
2. **Un contrato nace solo cuando otra feature lo pide** (Milan: arrancar sin nada público). Agregarlo a `conventions.md`. [INFERENCIA] Dune podría avisar si un tipo de `Contracts/` que no es response HTTP no lo usa ninguna otra feature.
3. **Revisar pasamanos con el deletion test** (skill `codebase-design`). Candidato: `UserProfiles.FindAsync`, que consulta una tabla y copia dos campos. Si borrarlo no mueve complejidad a los llamadores, se queda.
4. **Separar en `Contracts/` lo que ve otra feature de las responses HTTP propias.** `TaskResponse`, `TaskListResponse` y `HealthStatusResponse` no los usa ninguna otra feature. [INFERENCIA] Va junto con la propuesta 1 o se descarta allí.
5. **Lo que ya está en el plan y este informe confirma:** paso 22 (un `DbContext` por feature; sacar `AppIdentityUser` de `Contracts`; decidir FKs y la transacción del registro) y pasos 24 y 25 (eventos de integración con outbox en lugar de la cascada de la base).

## Qué quedó sin comprobar

- Las citas textuales salen del resumen de WebFetch, no del texto original letra por letra.
- El webinar de Bogard sobre IA: la grabación devolvió 404; solo se leyó el anuncio.
- Lo que Ousterhout dijo sobre IA: no hay transcripción; solo la paráfrasis del editor.
- Las charlas en video de Pocock: se usaron solo su artículo y sus skills.
- Qué tipos contiene `BuildingBlocks` en el repo de Grzybek.
- Derek Comartin, Vaughn Vernon y Eric Evans sobre nombres: no apareció material primario.
- Una fuente primaria contra los nombres "Manager", "Service" y "Helper": no apareció.
- Chapsas y ardalis sobre slices dentro de módulos: no apareció texto suyo.
- Las cifras del paper de CodeScene: solo resúmenes.
