using Microsoft.CodeAnalysis;

namespace Dune.Analyzers;

internal static class DuneDiagnostics
{
    public static readonly DiagnosticDescriptor DomainDependency = new(
        id: "DUNE0001",
        title: "Domain tiene dependencia prohibida",
        messageFormat: "Domain solo admite BCL y Shared.Domain/Domain propio; '{0}' no está permitido aquí",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Los modelos puros de cada feature no dependen de ASP.NET, EF, Identity, Serilog ni OpenTelemetry.");

    public static readonly DiagnosticDescriptor EndpointPersistence = new(
        id: "DUNE0002",
        title: "Endpoint accede a persistencia",
        messageFormat: "Los endpoints solo adaptan HTTP y llaman a su slice; '{0}' no está permitido en endpoints",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Los endpoints no tocan EF, Persistence ni UserManager.");

    public static readonly DiagnosticDescriptor InfrastructureModule = new(
        id: "DUNE0003",
        title: "Infrastructure conoce el interior de una feature",
        messageFormat: "Infrastructure solo conoce Domain y Contracts de las features; '{0}' no está permitido aquí",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Infrastructure es fontanería: no registra ni invoca features. Cada feature es su propio composition root y Program.cs las lista.");

    public static readonly DiagnosticDescriptor CrossFeature = new(
        id: "DUNE0004",
        title: "Una feature usa el interior de otra",
        messageFormat: "Una feature solo usa el Contracts de otra; '{0}' es interior de otra feature",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Entre features solo se usa Contracts (ADR-0004, punto 3). La regla mira el tipo nombrado, el tipo de cada operación (cada resultado, aunque no se guarde en una variable) y el tipo que declara cada miembro usado y su firma (también extensiones y los métodos que el compilador busca solo, como GetEnumerator o Deconstruct). Una subcarpeta de Contracts es interior.");

    public static readonly DiagnosticDescriptor NamespacePath = new(
        id: "DUNE0005",
        title: "Namespace no coincide con la ruta",
        messageFormat: "El namespace '{0}' no coincide con la ruta; se esperaba '{1}'",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "El namespace de todo tipo de primer nivel (clase, struct, record, record struct, interface, enum y delegate) coincide con la ruta relativa de la feature. Solo el Program.cs de cada host (backend/Program.cs y backend/Hosts/<Host>/Program.cs) queda en el namespace global.");

    public static readonly DiagnosticDescriptor DbSetShadow = new(
        id: "DUNE0006",
        title: "DbSet oculta miembro base con new",
        messageFormat: "No existe un segundo User ni un new DbSet que oculte Identity.Users; '{0}' oculta un DbSet base",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prohíbe shadowing de DbSet.");

    public static readonly DiagnosticDescriptor OwnerPredicate = new(
        id: "DUNE0007",
        title: "Carga individual combina Id y propietario",
        messageFormat: "La carga por Id no combina identidad del recurso con propietario; carga por Id y decide owner-or-permission después",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "El filtro por propietario solo vale para la lista propia; el recurso individual se carga por Id y luego devuelve 404/403.");

    public static readonly DiagnosticDescriptor SuffixPlacement = new(
        id: "DUNE0008",
        title: "Sufijo fuera de su ubicación",
        messageFormat: "El nombre '{0}' tiene un significado único y no pertenece aquí; se esperaba '{1}'",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Sin Module ni Endpoints compartidos; <Slice>Endpoint en la carpeta de su slice; <Feature>Feature en la raíz; Tests junto a su slice o en backend/Tests; Request/Response/Validator fuera de Domain.");

    public static readonly DiagnosticDescriptor NoComments = new(
        id: "DUNE0009",
        title: "Comentario en código propio",
        messageFormat: "El código propio no lleva comentarios",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Los comentarios se prohíben en backend y dune salvo el código generado por herramientas: las migraciones EF de Infrastructure y de cada feature, y desde el paso 27 los handlers que Wolverine genera con codegen write en backend/Internal/Generated/WolverineHandlers/.");

    public static readonly DiagnosticDescriptor OneTypePerFile = new(
        id: "DUNE0010",
        title: "Más de un tipo en un archivo",
        messageFormat: "'{0}' va en su propio archivo; si solo lo usa '{1}', se declara 'file' o anidado y privado",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un archivo declara un solo tipo de primer nivel. Un tipo auxiliar que usa solo ese tipo queda en el mismo archivo con el modificador file o anidado. Las migraciones generadas quedan exentas.");

    public static readonly DiagnosticDescriptor ApiRoute = new(
        id: "DUNE0011",
        title: "Ruta fuera del antiforgery de /api",
        messageFormat: "{0}; el antiforgery solo cubre las escrituras bajo /api",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "La cookie de sesión viaja sola, así que toda escritura pasa por el middleware de antiforgery, que cubre solo /api. Cada grupo del host empieza con /api, las rutas de escritura se mapean solo en el grupo de su feature y cada slice recibe ese grupo. La única excepción es el grupo /internal/v1 de Infrastructure/Http/InternalApi.cs: lo llaman otros procesos sin cookie y el gateway no lo publica.");

    public static readonly DiagnosticDescriptor VersionedApi = new(
        id: "DUNE0012",
        title: "Ruta fuera de la API versionada",
        messageFormat: "{0}; toda ruta de /api cuelga del grupo versionado de Infrastructure",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un solo grupo versionado, /api/v{version:apiVersion}, declara la versión de la API en Infrastructure. Cada feature recibe ese RouteGroupBuilder y no crea su propio version set. Un grupo con la versión en la ruta pero sin version set responde con cualquier versión.");

    public static readonly DiagnosticDescriptor ConfigurationAccess = new(
        id: "DUNE0013",
        title: "Lectura suelta de configuración",
        messageFormat: "'{0}' lee configuración suelta; declarala en un tipo de options con BindConfiguration y validador",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "La configuración entra solo por options validadas al arranque (AddOptionsWithValidateOnStart + BindConfiguration). El backend no nombra IConfiguration ni sus implementaciones y no lee variables de entorno; los tests quedan fuera.");

    public static readonly DiagnosticDescriptor ErrorOutsideCatalog = new(
        id: "DUNE0014",
        title: "Error fuera del catálogo",
        messageFormat: "'Error.{0}' arma un error fuera del catálogo; declaralo en un <Feature>Errors.cs o en Shared/Http",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un Error de ErrorOr se crea solo en un archivo *Errors.cs: el código es parte del contrato HTTP y sale de un catálogo. Los handlers devuelven los errores del catálogo; los tests y las migraciones quedan fuera.");

    public static readonly DiagnosticDescriptor ErrorCodePrefix = new(
        id: "DUNE0014",
        title: "Código de error con prefijo ajeno",
        messageFormat: "El código '{0}' no empieza con '{1}', el prefijo de {2}",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "En un catálogo de feature, el código fijo de un error empieza con el nombre de la clase sin 'Errors' (TaskErrors → Task.). Los errores de validación usan el nombre del campo y quedan fuera; también un código que no es constante.");

    public static readonly DiagnosticDescriptor ContractKind = new(
        id: "DUNE0015",
        title: "Tipo no admitido en Contracts",
        messageFormat: "'{0}' no es un contrato; en Contracts/ van {1}, records *Details/*Summary/*Info/*Response, *Permissions o *EndpointNames con constantes y *Extensions",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Contracts/ es lo único que ve otra feature, y cada clase de tipo se reconoce por su nombre: una sola puerta I<Feature>Module, datos con un sufijo de la lista cerrada (Details, Summary, Info), responses HTTP, constantes y métodos de extensión. Desde el paso 22 no hay excepciones: AppIdentityUser vive en Auth/Persistence.");

    public static readonly DiagnosticDescriptor ContractDoorSignature = new(
        id: "DUNE0015",
        title: "La puerta de la feature expone un tipo interno",
        messageFormat: "La puerta '{0}' expone '{1}'; su firma usa la BCL, ErrorOr, Shared y datos *Details/*Summary/*Info",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Los métodos de I<Feature>Module devuelven y reciben datos con propósito, no responses HTTP ni entidades. Response queda solo para HTTP.");

    public static readonly DiagnosticDescriptor ContractUnused = new(
        id: "DUNE0015",
        title: "Contrato que ninguna otra feature usa",
        messageFormat: "'{0}' está en Contracts/ pero ninguna otra feature lo usa; un contrato nace cuando otra feature lo pide",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Cada tipo de Contracts/ y cada método de la puerta tiene un uso desde otra feature. El uso desde la propia feature, Infrastructure o los tests no cuenta.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor IdempotentPost = new(
        id: "DUNE0016",
        title: "POST sin idempotencia",
        messageFormat: "'{0}' mapea un POST sin encadenar WithIdempotency<TRequest>(); el cliente no puede repetir la intención con Idempotency-Key",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Todo MapPost de un *Endpoint.cs de una feature encadena WithIdempotency<TRequest>(), así un reintento con la misma clave no crea un segundo recurso. Auth queda fuera: sus POST no crean recursos de negocio o ya son idempotentes.");

    public static readonly DiagnosticDescriptor AuditBypass = new(
        id: "DUNE0017",
        title: "Escritura masiva sobre una entidad auditada",
        messageFormat: "'{0}' sobre '{1}' no pasa por SaveChanges y no deja fila en AuditEntries; cargá la entidad, cambiala y guardala con SaveChangesAsync",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ExecuteUpdate y ExecuteDelete no pasan por el ChangeTracker, entonces AuditInterceptor no los ve y no dejan fila en AuditEntries. Desde el paso 24 tampoco dejan evento: OutboxInterceptor solo convierte en filas del outbox los eventos de las entidades del ChangeTracker. Una entidad que implementa IAuditable se cambia solo con SaveChanges; las demás (Session, IdempotencyKey, OutboxMessage) pueden usar escrituras masivas.");

    public static readonly DiagnosticDescriptor MigrationRename = new(
        id: "DUNE0018",
        title: "Migración que renombra",
        messageFormat: "'{0}' usa {1}: el código del release anterior sigue leyendo el nombre viejo; agregá lo nuevo, copiá los datos y borrá lo viejo en una migración Contract posterior",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un rename rompe el código que todavía corre durante un despliegue por tandas. EF genera RenameColumn también cuando se borra una columna y se agrega otra del mismo tipo. Un RenameTable que solo cambia el schema no es un rename: va en una migración Move.");

    public static readonly DiagnosticDescriptor MigrationDropOutsideContract = new(
        id: "DUNE0018",
        title: "Borrado fuera de una migración Contract",
        messageFormat: "'{0}' usa {1}: un borrado va solo en una migración cuyo nombre empieza con Contract, en un release posterior al que dejó de usar lo borrado",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Nunca destructivo en el mismo release (expand/contract). DropColumn, DropTable, DropIndex, DropSequence y DropSchema viven en una migración Contract propia.");

    public static readonly DiagnosticDescriptor MigrationContractMixed = new(
        id: "DUNE0018",
        title: "Migración Contract con otras operaciones",
        messageFormat: "'{0}' es una migración Contract y usa {1}: una migración Contract solo borra",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "La migración Contract es el release que borra lo que ya nadie usa. Lo que agrega o cambia va en otra migración, de otro release.");

    public static readonly DiagnosticDescriptor MigrationIndexNotConcurrent = new(
        id: "DUNE0018",
        title: "Índice sin CONCURRENTLY sobre una tabla existente",
        messageFormat: "'{0}' usa {1} sobre una tabla que ya existe sin IsCreatedConcurrently(): CREATE INDEX frena las escrituras de la tabla hasta terminar",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un índice sobre una tabla con datos se crea con CREATE INDEX CONCURRENTLY (HasIndex(...).IsCreatedConcurrently() de Npgsql), que no bloquea escrituras. Un índice de una tabla creada en la misma migración no lo necesita.");

    public static readonly DiagnosticDescriptor MigrationNotNullOnExisting = new(
        id: "DUNE0018",
        title: "NOT NULL sobre una columna existente",
        messageFormat: "'{0}' usa {1} para pasar a NOT NULL una columna que admitía NULL: EF rellena los NULL con un valor inventado y el código del release anterior todavía puede escribir NULL",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Una columna nueva entra nullable; el backfill la llena con datos reales y el código deja de escribir NULL antes de cualquier restricción.");

    public static readonly DiagnosticDescriptor MigrationTypeChange = new(
        id: "DUNE0018",
        title: "Cambio de tipo de una columna existente",
        messageFormat: "'{0}' usa {1} para cambiar el tipo de una columna existente: el release anterior sigue leyendo y escribiendo el tipo viejo; agregá una columna nueva y seguí las etapas de expand/contract",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "PostgreSQL reescribe la tabla con ACCESS EXCLUSIVE y el código del release anterior espera el tipo viejo. Un cambio de tipo es una columna nueva, un backfill y un Contract.");

    public static readonly DiagnosticDescriptor MigrationMoveOutsideMove = new(
        id: "DUNE0018",
        title: "Cambio de schema fuera de una migración Move",
        messageFormat: "'{0}' usa {1} para mover una tabla de schema: un cambio de schema va solo en una migración cuyo nombre empieza con Move, en un release posterior al que agregó el schema nuevo al Search Path",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "El release anterior nombra las tablas sin schema y las encuentra por search_path. Mover una tabla es seguro solo si ese release ya tenía el schema nuevo en su Search Path; la migración Move hace eso y nada más.");

    public static readonly DiagnosticDescriptor MigrationMoveMixed = new(
        id: "DUNE0018",
        title: "Migración Move con otras operaciones",
        messageFormat: "'{0}' es una migración Move y usa {1}: una migración Move solo crea schemas (EnsureSchema) y mueve tablas sin renombrarlas (RenameTable con newSchema)",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Cada migración hace una sola cosa: lo que agrega o cambia va en otra migración, de otro release.");

    public static readonly DiagnosticDescriptor MigrationMoveWithoutWayBack = new(
        id: "DUNE0018",
        title: "Migración Move sin vuelta en el Down",
        messageFormat: "'{0}' usa {1} en una migración Move sin volver la tabla a su schema: el Down de una migración Move solo lleva, por cada tabla, RenameTable con newSchema igual al schema de origen (public si el Up no lo nombra)",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "El Down que genera la CLI para un cambio de schema omite newSchema y no mueve nada: solo borra la fila del historial. Se completa a mano con el schema de origen.");

    public static readonly DiagnosticDescriptor MigrationMoveWithoutSchema = new(
        id: "DUNE0018",
        title: "Migración Move sin su schema",
        messageFormat: "'{0}' usa {1} para mover una tabla a un schema que su Up no crea: una migración Move lleva EnsureSchema de cada schema de destino",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ALTER TABLE ... SET SCHEMA falla si el schema no existe. La CLI genera EnsureSchema para cada schema nuevo del modelo; una migración Move escrita o editada a mano puede perderlo.");

    public static readonly DiagnosticDescriptor ReadSliceWrites = new(
        id: "DUNE0019",
        title: "Slice de lectura que escribe",
        messageFormat: "'{0}' es una slice de lectura (su endpoint solo mapea GET) y usa {1}: una lectura no escribe en la base",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "CQRS liviano: una slice cuyo endpoint solo mapea GET no llama a SaveChanges, ExecuteUpdate, ExecuteDelete, ExecuteSql* ni a Add, Update, Remove o Attach de un DbSet o del DbContext.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor ReadSliceLoadsEntity = new(
        id: "DUNE0019",
        title: "Slice de lectura que carga una entidad",
        messageFormat: "'{0}' es una slice de lectura y carga la entidad '{1}' con {2}: proyectá solo sus columnas a su modelo de lectura",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "CQRS liviano: una slice cuyo endpoint solo mapea GET no materializa una clase del Domain de una feature desde un IQueryable ni con Find, ni la pasa entera dentro de un Select (EF la trae completa y la rastrea); proyecta sus columnas a lo que responde. Users/GetUser es la única excepción hasta que proyecte.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
    public static readonly DiagnosticDescriptor PublicInterior = new(
        id: "DUNE0020",
        title: "Tipo public fuera de Contracts",
        messageFormat: "'{0}' es public fuera de Contracts/: el interior de una feature es internal",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Dentro de backend/Features, public significa contrato: solo los tipos de Contracts/ son public y todo lo demás es internal (ADR-0004, punto 1). Así el compilador impide que un contrato exponga un tipo interior en su firma. Excepción del paso 27: una saga de Wolverine (un tipo que hereda Wolverine.Saga) y los tipos de la firma de sus miembros públicos son public, porque Wolverine solo descubre handlers públicos y C# exige que esa firma use tipos públicos. Otra feature igual no los puede usar (DUNE0004).");

    public static readonly DiagnosticDescriptor ContractNotPublic = new(
        id: "DUNE0020",
        title: "Contrato que no es public",
        messageFormat: "'{0}' está en Contracts/ y no es public: lo que ve otra feature es public",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Dentro de backend/Features, public y Contracts/ son lo mismo en las dos direcciones: un tipo de Contracts/ es public.");

    public static readonly DiagnosticDescriptor DbContextOutsidePersistence = new(
        id: "DUNE0021",
        title: "DbContext fuera de Persistence",
        messageFormat: "'{0}' deriva de DbContext y vive fuera de Features/<Feature>/Persistence/ y de Infrastructure/Persistence/",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Propiedad de datos: cada feature mapea sus tablas en su propio DbContext, dentro de su Persistence/. Infrastructure/Persistence guarda solo PlatformDbContext y el AppDbContext viejo, dueño de las migraciones anteriores al paso 22.");

    public static readonly DiagnosticDescriptor SecondDbContextInFeature = new(
        id: "DUNE0021",
        title: "Más de un DbContext en una feature",
        messageFormat: "La feature {0} tiene {1} tipos que derivan de DbContext ({2}): una feature tiene a lo sumo uno",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un DbContext por feature: sus tablas, su carpeta de migraciones y su historial. Un segundo contexto parte la propiedad de los datos dentro de la feature.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor ExcludeFromMigrationsOutsideInfrastructure = new(
        id: "DUNE0021",
        title: "ExcludeFromMigrations fuera de Infrastructure/Persistence",
        messageFormat: "ExcludeFromMigrations se usa fuera de Infrastructure/Persistence: una feature no mapea una tabla que es de otro contexto",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Mapear una tabla sin migrarla es leer o escribir la tabla de otro contexto. La única tabla compartida es AuditEntries, y la mapea MapSharedAuditEntries de Infrastructure/Persistence para que la fila de auditoría viaje en el mismo SaveChanges. OutboxMessages e InboxMessages no son compartidas: MapOutbox() y MapInbox() las crean en el schema de cada contexto que las llama, y ese contexto las migra.");

    public static readonly DiagnosticDescriptor TransactionScopeOutsideHelper = new(
        id: "DUNE0021",
        title: "TransactionScope fuera de CrossContextTransaction",
        messageFormat: "new TransactionScope fuera de Infrastructure/Persistence/CrossContextTransaction.cs: una escritura entre contextos abre su transacción con CrossContextTransaction.Begin()",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un TransactionScope sin configurar usa Serializable. CrossContextTransaction fija ReadCommitted y el flujo async; es el único lugar que crea uno.");

    public static readonly DiagnosticDescriptor ConfigurationOfAnotherOwner = new(
        id: "DUNE0021",
        title: "Configuración de otro dueño",
        messageFormat: "El contexto aplica una configuración que no es de su dueño, o todas las del ensamblado: cada contexto aplica con ApplyConfiguration solo las configuraciones de su feature (o de Infrastructure, para PlatformDbContext)",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Aplicar la configuración de otra tabla es mapearla y migrarla: el contexto se vuelve dueño de una tabla ajena. ApplyConfigurationsFromAssembly trae las configuraciones de todas las features. Excepción: un contexto de feature aplica configuraciones de Infrastructure solo a través de sus helpers (MapSharedAuditEntries, MapOutbox, MapInbox), porque el ApplyConfiguration se escribe en Infrastructure. MapOutbox y MapInbox dejan sus tablas en el schema de la feature y las migra ella; una feature no escribe ApplyConfiguration de una configuración de Infrastructure.");
    public static readonly DiagnosticDescriptor RemoteCallInTransaction = new(
        id: "DUNE0022",
        title: "Llamada remota dentro de una transacción entre contextos",
        messageFormat: "'{0}' llama por HTTP a otro proceso ({1}) dentro de CrossContextTransaction.Begin(): la request no se suma a la transacción y nada lo avisa; hacé la llamada remota antes de abrir la transacción, como DeleteUser",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un TransactionScope abarca solo las conexiones a la base de este proceso. Si la transacción se revierte, lo que hizo el otro proceso queda hecho; mientras la request espera, la transacción mantiene abiertas la conexión y sus candados. Cuenta como llamada remota una invocación sobre HttpClient o HttpMessageInvoker, sobre un cliente tipado (un tipo con un HttpClient en su constructor) o un método de extensión de HttpClient como SendRemoteAsync. Límite: mira solo el método que abre la transacción. No ve a través de un filtro de endpoint: IdempotencyFilter abre el scope y la slice corre adentro.");

    public static readonly DiagnosticDescriptor EventHandlerCalledDirectly = new(
        id: "DUNE0023",
        title: "Consumidor de eventos llamado directo",
        messageFormat: "'{0}' llama al consumidor de eventos '{1}' fuera de OutboxRelay: su efecto puede correr antes de que se confirme el cambio; anotá el evento en la entidad y dejá que el relay lo entregue",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un IEventHandler<T> solo lo llama OutboxRelay (backend/Infrastructure/Messaging/OutboxRelay.cs), que entrega filas ya confirmadas del outbox. Llamarlo desde otro lugar, por ejemplo después de SaveChangesAsync o en un interceptor, publica antes del commit: dentro de un TransactionScope, SaveChanges no confirma. Cuenta como llamada la invocación de un método de un tipo que implementa IEventHandler<T> (también un parámetro genérico restringido a él) y la creación de ese tipo con new. Los tests quedan fuera. Límite: no ve una llamada por reflexión ni por un delegado guardado, como EventHandlerRegistration.Dispatch. Paso 25: un consumidor de un evento de integración implementa IIntegrationEventHandler<T>, otra interfaz, y lo llama el handler de Wolverine en Infrastructure/Messaging; DUNE0023 no lo mira.");

    public static readonly DiagnosticDescriptor ReadSliceCallsRemote = new(
        id: "DUNE0024",
        title: "Lectura que llama a otro proceso",
        messageFormat: "'{0}' es una slice de lectura (su endpoint solo mapea GET) y llama por HTTP a otro proceso por {1}: una lectura no llama a otro proceso; leé su proyección local alimentada por eventos",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 24.1: una lectura que necesita datos de una feature de otro proceso lee su proyección local, que se pone al día con los eventos de ese proceso; no lo llama en cada request. Cuenta como lectura una slice cuyo endpoint solo mapea GET, como DUNE0019. Cuenta como cliente remoto HttpClient, HttpMessageInvoker o un cliente tipado (un tipo con un HttpClient en su constructor), como DUNE0022. La dependencia se sigue por los constructores de un tipo de la slice y de los tipos de su misma feature, por ejemplo ListProjects -> ProjectResponses -> TaskCountsClient. Los tests quedan fuera. Límite: no ve un cliente pedido con IServiceProvider ni uno detrás de una interfaz, por ejemplo un contrato de otra feature o de otro ensamblado.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor BusOutsideMessaging = new(
        id: "DUNE0025",
        title: "Wolverine fuera de Infrastructure/Messaging o como mediator",
        messageFormat: "'{0}' usa Wolverine {1}: el bus es transporte y vive en backend/Infrastructure/Messaging; una feature publica con IntegrationEventPublisher, consume con IIntegrationEventHandler<T> y solo nombra Wolverine en el archivo de un orquestador (un tipo que hereda Wolverine.Saga o Wolverine.TimeoutMessage)",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pasos 25 y 27 (ADR-0004 punto 9): Wolverine es el bus elegido. Un nombre que resuelve a un namespace Wolverine o JasperFx (un using, un tipo, un miembro o un método de extensión) solo aparece en backend/Infrastructure/Messaging/ y, desde el paso 27, en el archivo de un orquestador: el que declara un tipo que hereda Wolverine.Saga (la saga orquestada, con estado persistido) o Wolverine.TimeoutMessage (su plazo). Ninguna otra slice conoce el bus. Además, nadie llama a un método Invoke* de un tipo de Wolverine (IMessageBus.InvokeAsync y sus variantes), ni siquiera Infrastructure/Messaging: es el modo mediator de Wolverine, que ADR-0004 descarta. Los tests y las migraciones quedan fuera. Límite: no ve un tipo de Wolverine usado por reflexión ni detrás de var sin nombrarlo, por ejemplo el resultado de un método del repo que devuelve IMessageBus.");

    public static readonly DiagnosticDescriptor AppendOnlyWrite = new(
        id: "DUNE0026",
        title: "Cambio o borrado de una fila append-only",
        messageFormat: "'{0}' cambia o borra filas de '{1}', que implementa IAppendOnly: sus filas solo se agregan",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 28: un historial auditable (IAppendOnly, en Shared/Domain) solo crece. Su código no llama a Update, UpdateRange, Remove ni RemoveRange (de un DbSet o del DbContext) ni a ExecuteUpdate, ExecuteUpdateAsync, ExecuteDelete o ExecuteDeleteAsync sobre ese tipo. La base lo refuerza con un trigger BEFORE UPDATE OR DELETE que tira error. Los tests y las migraciones quedan fuera. Límite: no ve SQL crudo ni un cambio hecho cargando la entidad con el ChangeTracker y guardando, por eso sus propiedades son init (la otra mitad de la regla).");

    public static readonly DiagnosticDescriptor AppendOnlySetter = new(
        id: "DUNE0026",
        title: "Propiedad con set en un tipo append-only",
        messageFormat: "'{0}' tiene set en '{1}', que implementa IAppendOnly: sus propiedades son init para que nadie cambie una fila cargada",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 28: una fila append-only se crea completa y no cambia. Sus propiedades tienen get e init (o solo get); un set público o privado permitiría cargarla, cambiarla y guardarla con SaveChanges, que DUNE0026 no ve como llamada.");

    public static readonly DiagnosticDescriptor HttpClientOutsideInfrastructure = new(
        id: "DUNE0027",
        title: "Cliente HTTP fuera de Infrastructure/Http",
        messageFormat: "'{0}' {1}: un cliente hacia otro proceso se registra con AddRemoteClient<TClient>(servicio, RemoteRetry), que le pone la política de resiliencia; solo backend/Infrastructure/Http arma clientes HTTP y nombra Polly",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 29: todo cliente saliente pasa por la misma política (bulkhead, timeout total, reintento, circuit breaker y timeout por intento), que vive en backend/Infrastructure/Http/RemoteServices. Fuera de esa carpeta es error crear un HttpClient, un HttpMessageInvoker o un HttpMessageHandler (HttpClientHandler, SocketsHttpHandler o un DelegatingHandler propio), llamar a los registros de IHttpClientFactory (AddHttpClient, ConfigureHttpClientDefaults, ConfigurePrimaryHttpMessageHandler, AddHttpMessageHandler y los demás de HttpClientFactoryServiceCollectionExtensions y HttpClientBuilderExtensions) o a los de Microsoft.Extensions.Http.Resilience (AddResilienceHandler, AddStandardResilienceHandler), y nombrar un símbolo de los namespaces Polly o Microsoft.Extensions.Http.Resilience. Así ninguna slice arma un cliente sin resiliencia ni conoce Polly: un circuito abierto le llega como RemoteServiceUnavailableException. Los tests y las migraciones quedan fuera, y el gateway también (no está en backend/ y YARP trae su cliente). Límite: no ve un cliente pedido a IHttpClientFactory.CreateClient con un nombre que nadie registró.");
}
