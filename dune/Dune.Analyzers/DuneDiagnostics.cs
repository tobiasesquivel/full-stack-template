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
        description: "El namespace de todo tipo de primer nivel (clase, struct, record, record struct, interface, enum y delegate) coincide con la ruta relativa de la feature. Solo el Program.cs de cada host (backend/Hosts/<Host>/Program.cs) queda en el namespace global.");

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
        description: "Los comentarios se prohíben en backend y dune salvo el código generado por herramientas: las migraciones EF de Infrastructure y de cada feature, y desde el paso 27 los handlers que Wolverine genera con codegen write en backend/Hosts/<Host>/Internal/Generated/WolverineHandlers/ de cualquier host (desde el paso T1, sin lista de hosts).");

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
        description: "Desde la reorganización (parte 4), ningún Map* de ASP.NET (MapGet incluido) se escribe en una carpeta técnica de una feature (Contracts/, Domain/, Persistence/, Access/, MessageNames/), aunque el nombre del archivo parezca de una migración: las rutas viven en una slice, donde las miran DUNE0019, DUNE0024 y DUNE0028. La cookie de sesión viaja sola, así que toda escritura pasa por el middleware de antiforgery, que cubre solo /api. Cada grupo del host empieza con /api, las rutas de escritura se mapean solo en el grupo de su feature y cada slice recibe ese grupo. La única excepción es el grupo /internal/v1 de Infrastructure/Http/Remote/InternalApi.cs: lo llaman otros procesos sin cookie y el gateway no lo publica.");

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
        description: "Todo MapPost de un *Endpoint.cs de una feature encadena WithIdempotency<TRequest>(), así un reintento con la misma clave no crea un segundo recurso. Desde el paso T1 la regla no nombra features: un endpoint cuyo POST no crea un recurso de negocio o ya es idempotente lo declara en su clase con [DuneExemption(DuneCheck.IdempotentPost, \"motivo\")] (hoy, los cuatro POST de Auth). Un POST nuevo sin esa línea vuelve a fallar.");

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
        description: "CQRS liviano: una slice cuyo endpoint solo mapea GET no materializa una clase del Domain de una feature desde un IQueryable ni con Find, ni la pasa entera dentro de un Select (EF la trae completa y la rastrea); proyecta sus columnas a lo que responde. Desde el paso T1 la regla no nombra slices: una slice que todavía carga la entidad lo declara en su clase <Slice>Endpoint con [DuneExemption(DuneCheck.ReadSliceLoadsEntity, \"motivo\")] (hoy, Users/GetUser hasta que proyecte). Esa excepción no apaga \"Slice de lectura que escribe\", que tiene el mismo id.",
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
        messageFormat: "'{0}' deriva de DbContext y vive fuera de Features/<Feature>/Persistence/ y de la raíz de Infrastructure/Persistence/",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Propiedad de datos: cada feature mapea sus tablas en su propio DbContext, dentro de su Persistence/. La raíz de Infrastructure/Persistence guarda solo PlatformDbContext y el AppDbContext viejo, dueño de las migraciones anteriores al paso 22; sus subcarpetas (Audit/, Idempotency/, Initialization/, Health/, Transactions/) no tienen contextos.");

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
        description: "Mapear una tabla sin migrarla es leer o escribir la tabla de otro contexto. La única tabla compartida es AuditEntries, y la mapea MapSharedAuditEntries (Infrastructure/Persistence/Audit/SharedAuditEntries.cs, el único archivo fuera de la raíz de Infrastructure/Persistence que puede llamar ExcludeFromMigrations) para que la fila de auditoría viaje en el mismo SaveChanges. OutboxMessages e InboxMessages no son compartidas: MapOutbox() y MapInbox() las crean en el schema de cada contexto que las llama, y ese contexto las migra.");

    public static readonly DiagnosticDescriptor TransactionScopeOutsideHelper = new(
        id: "DUNE0021",
        title: "TransactionScope fuera de CrossContextTransaction",
        messageFormat: "new TransactionScope fuera de Infrastructure/Persistence/Transactions/CrossContextTransaction.cs: una escritura entre contextos abre su transacción con CrossContextTransaction.Begin()",
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
        description: "Un TransactionScope abarca solo las conexiones a la base de este proceso. Si la transacción se revierte, lo que hizo el otro proceso queda hecho; mientras la request espera, la transacción mantiene abiertas la conexión y sus candados. Cuenta como llamada remota una invocación sobre HttpClient o HttpMessageInvoker, sobre un cliente tipado (un tipo con un HttpClient en su constructor) o un método de extensión de HttpClient como SendRemoteAsync. Límite: mira solo el método que abre la transacción, y la transacción que Wolverine abre alrededor de un handler del mediator (paso 29.2, el otro diagnóstico DUNE0022). No ve a través de un filtro de endpoint: IdempotencyFilter abre el scope y la slice corre adentro.");

    public static readonly DiagnosticDescriptor RemoteCallInMediatorTransaction = new(
        id: "DUNE0022",
        title: "Llamada remota dentro de la transacción de un handler del mediator",
        messageFormat: "'{0}' llama por HTTP a otro proceso ({1}) dentro de '{2}', un handler registrado con MediatorTransaction.{3}: Wolverine abre la transacción antes del handler y la confirma después, así que la llamada queda adentro; registralo con MediatorTransaction.None o sacá la llamada del handler",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 29.2: en el monolito, un handler registrado con AddMediatorHandler<T>(MediatorTransaction.Atomic) o (MediatorTransaction.Eager) corre entero dentro de la transacción que abre Wolverine (MediatorTransactionPolicy). Una llamada remota en cualquier método del tipo del handler queda dentro de esa transacción, con los mismos riesgos que dentro de CrossContextTransaction.Begin(). Cuenta como llamada remota lo mismo que el otro diagnóstico DUNE0022. Límite: no ve una llamada remota hecha a través de otro servicio que el handler recibe.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor EventHandlerCalledDirectly = new(
        id: "DUNE0023",
        title: "Consumidor de eventos llamado directo",
        messageFormat: "'{0}' llama al consumidor de eventos '{1}' fuera de OutboxRelay: su efecto puede correr antes de que se confirme el cambio; anotá el evento en la entidad y dejá que el relay lo entregue",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un IEventHandler<T> solo lo llama OutboxRelay (backend/Infrastructure/Messaging/Outbox/OutboxRelay.cs), que entrega filas ya confirmadas del outbox. Llamarlo desde otro lugar, por ejemplo después de SaveChangesAsync o en un interceptor, publica antes del commit: dentro de un TransactionScope, SaveChanges no confirma. Cuenta como llamada la invocación de un método de un tipo que implementa IEventHandler<T> (también un parámetro genérico restringido a él) y la creación de ese tipo con new. Los tests quedan fuera. Límite: no ve una llamada por reflexión ni por un delegado guardado, como EventHandlerRegistration.Dispatch. Paso 25: un consumidor de un evento de integración implementa IIntegrationEventHandler<T>, otra interfaz, y lo llama el handler de Wolverine en Infrastructure/Messaging; DUNE0023 no lo mira.");

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
        title: "Wolverine fuera de Infrastructure/Messaging o mediator fuera del endpoint",
        messageFormat: "'{0}' usa Wolverine {1}: el bus vive en backend/Infrastructure/Messaging; una feature publica con IntegrationEventPublisher, consume con IIntegrationEventHandler<T>, nombra Wolverine en el archivo de un orquestador (un tipo que hereda Wolverine.Saga o Wolverine.TimeoutMessage) y, en el *Endpoint.cs de una slice, solo nombra IMessageBus para despachar con InvokeAsync<T>",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pasos 25 y 27 (ADR-0004 punto 9): Wolverine es el bus elegido. Un nombre que resuelve a un namespace Wolverine o JasperFx (un using, un tipo, un miembro o un método de extensión) solo aparece en backend/Infrastructure/Messaging/ y, desde el paso 27, en el archivo de un orquestador: el que declara un tipo que hereda Wolverine.Saga (la saga orquestada, con estado persistido) o Wolverine.TimeoutMessage (su plazo). Desde el paso 29.1, el *Endpoint.cs de una slice puede nombrar el namespace Wolverine en su using, el tipo IMessageBus y su InvokeAsync<T> genérico sin DeliveryOptions: es el modo mediator, y el endpoint despacha así el comando de su slice. Ninguna otra slice conoce el bus. Cualquier otro método Invoke* de un tipo de Wolverine (InvokeAsync sin <T>, con DeliveryOptions, InvokeForTenantAsync) es error en todo el producto, ni siquiera Infrastructure/Messaging lo llama, y fuera de un *Endpoint.cs también InvokeAsync<T>: un handler nunca invoca a otro handler. Los tests y las migraciones quedan fuera. Límite: no ve un tipo de Wolverine usado por reflexión ni detrás de var sin nombrarlo, por ejemplo el resultado de un método del repo que devuelve IMessageBus.");

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
        title: "Cliente HTTP fuera de Infrastructure/Http/Remote",
        messageFormat: "'{0}' {1}: un cliente hacia otro proceso se registra con AddRemoteClient<TClient>(servicio, RemoteRetry), que le pone la política de resiliencia; solo backend/Infrastructure/Http/Remote arma clientes HTTP y nombra Polly",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 29: todo cliente saliente pasa por la misma política (bulkhead, timeout total, reintento, circuit breaker y timeout por intento), que vive en backend/Infrastructure/Http/Remote/RemoteServices. Fuera de esa carpeta es error crear un HttpClient, un HttpMessageInvoker o un HttpMessageHandler (HttpClientHandler, SocketsHttpHandler o un DelegatingHandler propio), llamar a los registros de IHttpClientFactory (AddHttpClient, ConfigureHttpClientDefaults, ConfigurePrimaryHttpMessageHandler, AddHttpMessageHandler y los demás de HttpClientFactoryServiceCollectionExtensions y HttpClientBuilderExtensions) o a los de Microsoft.Extensions.Http.Resilience (AddResilienceHandler, AddStandardResilienceHandler), nombrar IHttpClientFactory (que da clientes sin la política) y nombrar un símbolo de los namespaces Polly o Microsoft.Extensions.Http.Resilience. Así ninguna slice arma un cliente sin resiliencia ni conoce Polly: un circuito abierto le llega como RemoteServiceUnavailableException. Los tests y las migraciones quedan fuera, y el gateway también (no está en backend/ y YARP trae su cliente). Límite: no ve un HttpClient pedido por constructor en un tipo que no se registró con AddRemoteClient (desde .NET 8, IHttpClientFactory registra un HttpClient por defecto sin la política); lo cubre la revisión.");

    public static readonly DiagnosticDescriptor TransactionOwner = new(
        id: "DUNE0029",
        title: "La slice no maneja la transacción",
        messageFormat: "'{0}' llama {1}: la transacción de un handler del mediator es de Wolverine; la slice no abre, confirma ni revierte, y llama SaveChangesAsync solo con await dentro de un try cuyo catch atrapa DbUpdateException",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso 29.2 (ADR-0004 punto 9): Wolverine abre, guarda y confirma alrededor de cada handler del monolito, según la clase con que se registra (AddMediatorHandler<T>(MediatorTransaction.Atomic | Eager | None)). Desde el paso T1 vale para toda feature de backend/Features/: dentro del tipo de un handler registrado, es error llamar CrossContextTransaction.Begin, DatabaseFacade.BeginTransaction(Async), CommitTransaction(Async), RollbackTransaction(Async), UseTransaction(Async), IDbContextTransaction.Commit(Async) o Rollback(Async), crear un TransactionScope, y llamar SaveChanges o SaveChangesAsync de un DbContext. La única excepción es el flush para traducir una restricción: await SaveChangesAsync dentro del bloque try de un try cuyo catch atrapa DbUpdateException o una subclase (CreateProject, UpdateProject). Quedan fuera las puertas <Feature>Module, los pasos de la saga, los consumidores del broker y los tests. Una feature cuyo proceso no tiene la integración de EF de Wolverine maneja su transacción y lo declara en su clase <Feature>Feature con [DuneExemption(DuneCheck.TransactionOwner, \"motivo\")] (hoy, Tasks); MediatorTransaction.None no alcanza como marca, porque en el monolito significa que Wolverine guarda al final. Límite: no ve SaveChanges llamado a través de otro servicio que el handler recibe.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MediatorEndpoint = new(
        id: "DUNE0028",
        title: "Endpoint de una slice que no despacha su comando por el mediator",
        messageFormat: "'{0}' {1}: el *Endpoint.cs de cada slice despacha con IMessageBus.InvokeAsync<T> exactamente un comando declarado en la carpeta de su slice, y nunca recibe ni construye el handler",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pasos 29.1 y 29.2 (ADR-0004 punto 9): las slices HTTP son handlers de Wolverine y su endpoint les llega por el mediator. Desde el paso T1 vale para toda feature: en backend/Features/<Feature>/<Slice>/*Endpoint.cs es error no llamar nunca a IMessageBus.InvokeAsync<T> (la forma genérica: sin <T> la respuesta se pierde), llamarlo más de una vez, despachar un mensaje cuyo tipo no está declarado en la carpeta de la misma slice, y recibir por parámetro o construir con new un tipo de la feature que tiene un método Handle (el handler): así el endpoint no puede saltear el middleware. Una slice sin caso de uso lo declara en su clase <Slice>Endpoint con [DuneExemption(DuneCheck.MediatorEndpoint, \"motivo\")]: hoy Auth/IssueCsrfToken, que solo escribe la cookie de antiforgery, y Health/GetLiveness y Health/GetReadiness, que responden sin mediator. Los tests quedan fuera. Límite: no ve un handler pedido por service location ni un mensaje que llega en una variable de tipo object.");

    public static readonly DiagnosticDescriptor BusNameOutsideMessageNames = new(
        id: "DUNE0030",
        title: "Nombre del bus fuera de MessageNames",
        messageFormat: "'{0}' {1}: el nombre, la versión, el exchange y la clave de ruteo de un mensaje del bus salen de una constante de Features/<Feature>/MessageNames/, que solo guarda clases estáticas con constantes",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reorganización, parte 4 (ADR-0004 punto 5): el contrato entre dos despliegues que se hablan por el bus es el nombre estable y la versión de cada mensaje, y su exchange. Cada feature guarda esos nombres en su carpeta técnica MessageNames/, una clase estática por exchange (TaskEvents, TaskCommands, AuthEvents) solo con constantes. En backend/Features/<Feature>/ es error pasarle a un método o a un constructor de TemplateApp.Infrastructure.Messaging un argumento name, version, exchange o routingKey (sin mirar mayúsculas: también los records RetiredBinding o IntegrationEventType) que no sea una constante declarada en MessageNames/ de la misma feature, tal cual o con una conversión implícita: un literal, una expresión, una conversión explícita, una constante de otra carpeta o de otra feature. También es error declarar en MessageNames/ (o en una subcarpeta) un tipo que no sea una clase estática de primer nivel solo con constantes. La cola (queue) no es un nombre de mensaje: es del consumidor y queda junto a él. Si una compilación tiene MessageNames/ y no encuentra en TemplateApp.Infrastructure.Messaging alguno de los seis registros (AddOutboxEvent, AddIntegrationEventPublication, AddIntegrationEventHandler, AddIntegrationCommand, AddSagaMessage, AddRetiredBinding) con todos sus parámetros de nombre (name y version; también exchange, salvo AddOutboxEvent; exchange y routingKey en AddRetiredBinding; sin mirar mayúsculas), avisa que quedó ciega para ese registro. Los tests y las migraciones quedan fuera.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MediatorHandlerShape = new(
        id: "DUNE0031",
        title: "Handler del mediator fuera de su forma",
        messageFormat: "'{0}' {1}: el handler de una slice es <Mensaje>.Handler, anidado en el record partial <Slice>Command o <Slice>Query, en el archivo <Mensaje>Handler.cs",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ADR-0004 punto 2 (desde el paso T1, en toda feature): el mensaje de una slice se llama <Slice>Command si cambia estado y <Slice>Query si solo lee, y su handler vive en su propio archivo, <Mensaje>Handler.cs. Wolverine solo acepta handlers públicos y un handler público de primer nivel no compila con dependencias internal (CS0051), así que el handler es un tipo público anidado: Handler, dentro de la mitad partial del record del mensaje. En backend/Features/<Feature>/<Slice>/ es error que un tipo público anidado con un método Handle esté dentro de algo que no sea un record terminado en Command o Query, que no se llame Handler, que no viva en <Mensaje>Handler.cs o que el record no se declare también en <Mensaje>.cs de la misma carpeta (así el mensaje y el handler quedan en archivos separados). Los tests quedan fuera. Límite: no decide si un mensaje cambia estado (DUNE0019 impide que una slice de lectura escriba) ni ve validaciones de entrada escritas en el handler; las cubre la revisión.");

    public static readonly DiagnosticDescriptor HostBoundary = new(
        id: "DUNE0032",
        title: "Un host compila algo que no le toca",
        messageFormat: "'{0}' {1}",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Límite de despliegue (pasos 23.2 y reorganización, parte 6): cada host es un proceso aparte y compila solo su carpeta y las features que despliega. Desde el paso T1 la regla no tiene tabla de hosts: un host es todo proyecto en backend/Hosts/<Host>/, y su .csproj declara qué features despliega con una de dos propiedades, DuneHostFeatures (las que compila, separadas por comas: el host de Tasks, Tasks,Health) o DuneHostExcludedFeatures (todas salvo esas: el monolito, Tasks). Ninguno compila *Tests.cs, Infrastructure/, Shared/, backend/Tests/ ni archivos de fuera de backend/, y el único proyecto que referencian es TemplateApp.Platform: falla un ensamblado TemplateApp.* o un archivo .dll que esté dentro del repo, se llame como se llame. La regla mira los archivos que el compilador recibe de verdad, después de MSBuild: lo que llega por un target, una condición, un Import o Directory.Build.* ya está resuelto, igual que las rutas con . o .., %2e o entidades XML. Lee la carpeta del proyecto, su ruta relativa a la raíz del repo y la declaración de sus features de MSBuildProjectDirectory, DuneProjectPath, DuneHostFeatures y DuneHostExcludedFeatures (Dune.props las expone); con DuneProjectPath reconoce un host sin mirar mayúsculas ni las carpetas de fuera del repo. Falla si un proyecto fuera de backend/Hosts/ declara alguna de las dos, si un host no declara ninguna de las dos o declara las dos, si declara en DuneHostFeatures una feature que no compila, si su .csproj no está en la raíz de backend/Hosts/<Host>/, y si una compilación con archivos de backend/Hosts/ no puede leer la carpeta de su proyecto. Acepta lo que genera el SDK en la carpeta intermedia del host (IntermediateOutputPath, también fuera del repo con --artifacts-path), salvo que esa carpeta apunte a otra parte del repo. Límite: no ve un enlace simbólico (lo ve check-host-items, con la ruta física) ni un build que no carga Dune, como el del Dockerfile.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor ExemptionWithoutReason = new(
        id: "DUNE0033",
        title: "Excepción de Dune sin motivo",
        messageFormat: "'{0}' {1}: una excepción de Dune es [DuneExemption(DuneCheck.<Chequeo>, \"motivo\")], con un miembro de DuneCheck y el motivo escrito",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso T1: las reglas que revisan features y slices no guardan sus nombres. Lo que una feature hace distinto lo declara ella, en el tipo que mira la regla, con [DuneExemption(DuneCheck.X, \"motivo\")] (backend/Shared/Architecture). Aceptan excepción IdempotentPost (DUNE0016) y MediatorEndpoint (DUNE0028) en la clase <Slice>Endpoint, ReadSliceLoadsEntity (DUNE0019, solo \"carga una entidad\") en la clase <Slice>Endpoint de su slice, y TransactionOwner (DUNE0029) en la clase <Feature>Feature. La excepción cuenta solo en el tipo de primer nivel que da nombre a su archivo (la clase <Slice>Endpoint o <Feature>Feature) y escrita en ese archivo: no en una clase anidada ni en otra parte de una clase partial. Es error un atributo con el motivo vacío o con un valor que no es un miembro de DuneCheck: esa excepción no exime nada. Límite: no avisa si una excepción quedó sin efecto, por ejemplo en otro tipo que el que mira su regla; la cubre la revisión.");

    public static readonly DiagnosticDescriptor FeatureDiscovery = new(
        id: "DUNE0034",
        title: "Feature que el host no descubre",
        messageFormat: "'{0}' {1}",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso T3: cada host descubre las features de su compilación (FeatureHosting.AddFeatures y MapFeatures, en Infrastructure/Hosting) y Program.cs no las nombra. Un host crea cada clase que implementa TemplateApp.Infrastructure.Hosting.IFeature, registra sus servicios y mapea sus rutas públicas e internas. Por eso, si una compilación tiene algún archivo de backend/Features/<Feature>/, es error que no exista TemplateApp.Features.<Feature>.<Feature>Feature en la propia compilación (no en una referencia), que sea estática, abstracta, no sea una clase o no sea sealed, que no implemente IFeature o que no tenga un constructor público sin parámetros: esa feature compilaría y el host no la arrancaría. También es error que implemente IFeature un tipo que no sea la clase <Feature>Feature de primer nivel, no genérica y no declarada con file, de backend/Features/<Feature>/<Feature>Feature.cs: el host lo arrancaría como una feature más, o no podría crear un genérico abierto. Si la compilación tiene features y no encuentra IFeature, avisa que quedó ciega. Revisa toda implementación de IFeature que recibe el compilador, también el código generado y los archivos de fuera de backend/ (como una carpeta intermedia fuera del repo). Decide por proyecto y no por el nombre del archivo: no corre en el proyecto de tests (DuneProjectPath backend/Tests/), donde FeatureHostingTests usa una IFeature propia; en cualquier otro proyecto revisa todo, también un archivo *Tests.cs que llegue al host y una feature que se llame Tests.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor FeaturePermissions = new(
        id: "DUNE0035",
        title: "Permiso fuera de la declaración de su feature",
        messageFormat: "'{0}' {1}",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Paso T4: cada feature declara sus permisos y qué rol recibe cada uno en backend/Features/<Feature>/Access/<Feature>Permissions.cs, una clase internal sealed que implementa TemplateApp.Infrastructure.Access.IFeaturePermissions. El host la descubre por reflexión (HostPermissions) y toma el nombre de la feature del nombre de la clase; Auth junta las declaraciones sin nombrar features: las del monolito al arrancar (RoleSeeder) y las de otro proceso por PUT /internal/v1/feature-permissions/<Feature>. Es error que implemente IFeaturePermissions un tipo que no sea la clase <Feature>Permissions de primer nivel, no genérica ni file, de ese archivo, que no sea sealed, sea estática o abstracta, o no tenga un constructor público sin parámetros. También es error escribir un literal \"Permissions.<Feature>.\" fuera de backend/Features/<Feature>/Access/<Feature>Permissions.cs: la feature usa la constante y nadie guarda una copia (como la TaskPermissionNames que tenía Auth). No corre en el proyecto de tests (DuneProjectPath backend/Tests/), que escribe los permisos esperados a mano. Límite: no ve un permiso armado con un string interpolado o concatenado.");
}
