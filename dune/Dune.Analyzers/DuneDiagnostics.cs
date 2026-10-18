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
        description: "Entre features solo se usa Contracts (ADR-0004, punto 3). La regla mira el tipo nombrado y también el tipo de cada expresión, para que var no la esquive.");

    public static readonly DiagnosticDescriptor NamespacePath = new(
        id: "DUNE0005",
        title: "Namespace no coincide con la ruta",
        messageFormat: "El namespace '{0}' no coincide con la ruta; se esperaba '{1}'",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "El namespace coincide con la ruta relativa de la feature.");

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
}
