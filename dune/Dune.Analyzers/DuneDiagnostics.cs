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
        description: "El namespace de todo tipo de primer nivel (clase, struct, record, record struct, interface, enum y delegate) coincide con la ruta relativa de la feature.");

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
        description: "Los comentarios se prohíben en backend y dune salvo las migraciones EF generadas de Infrastructure y de cada feature.");

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
        description: "La cookie de sesión viaja sola, así que toda escritura pasa por el middleware de antiforgery, que cubre solo /api. Cada grupo del host empieza con /api, las rutas de escritura se mapean solo en el grupo de su feature y cada slice recibe ese grupo.");

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
        description: "Contracts/ es lo único que ve otra feature, y cada clase de tipo se reconoce por su nombre: una sola puerta I<Feature>Module, datos con un sufijo de la lista cerrada (Details, Summary, Info), responses HTTP, constantes y métodos de extensión. AppIdentityUser es la única excepción hasta el paso 22.");

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
        description: "ExecuteUpdate y ExecuteDelete no pasan por el ChangeTracker, entonces AuditInterceptor no los ve. Una entidad que implementa IAuditable se cambia solo con SaveChanges; las demás (Session, IdempotencyKey) pueden usar escrituras masivas.");

    public static readonly DiagnosticDescriptor MigrationRename = new(
        id: "DUNE0018",
        title: "Migración que renombra",
        messageFormat: "'{0}' usa {1}: el código del release anterior sigue leyendo el nombre viejo; agregá lo nuevo, copiá los datos y borrá lo viejo en una migración Contract posterior",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Un rename rompe el código que todavía corre durante un despliegue por tandas. EF genera RenameColumn también cuando se borra una columna y se agrega otra del mismo tipo.");

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
        description: "Dentro de backend/Features, public significa contrato: solo los tipos de Contracts/ son public y todo lo demás es internal (ADR-0004, punto 1). Así el compilador impide que un contrato exponga un tipo interior en su firma.");

    public static readonly DiagnosticDescriptor ContractNotPublic = new(
        id: "DUNE0020",
        title: "Contrato que no es public",
        messageFormat: "'{0}' está en Contracts/ y no es public: lo que ve otra feature es public",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Dentro de backend/Features, public y Contracts/ son lo mismo en las dos direcciones: un tipo de Contracts/ es public.");
}
