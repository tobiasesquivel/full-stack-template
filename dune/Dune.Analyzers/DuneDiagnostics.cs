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
        messageFormat: "Los endpoints solo adaptan HTTP y llaman a su módulo; '{0}' no está permitido en endpoints",
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
        title: "Referencia entre implementations de features",
        messageFormat: "Una feature no llama a la implementation de otra; '{0}' solo se admite vía Domain/Contracts",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Las referencias entre features son explícitas a modelos de perfil y contratos compartidos.");

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
        description: "Module/Endpoints viven en la raíz de la feature; Tests bajo Tests/; Request/Response/Validator fuera de Domain.");

    public static readonly DiagnosticDescriptor NoComments = new(
        id: "DUNE0009",
        title: "Comentario en código propio",
        messageFormat: "El código propio no lleva comentarios",
        category: "Dune",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Los comentarios se prohíben en backend y dune salvo las migraciones EF generadas de Infrastructure y de cada feature.");
}
