# ADR-0006: Cada host descubre las features que compila

- Estado: aceptada
- Fecha: 2026-11-27
- Decide: el hilo del paso T3, en modo autónomo (`AGENTS.md`, "Modo autónomo"), sobre la decisión de Tobi "los hosts descubren las features (ADR)" (`docs/decisions.tsv`, fila `template` del 2026-11-27)
- Cambia: el punto 6 de [ADR-0004](0004-vertical-slices-deep-modules.md) ("Cada feature se registra sola desde su `<Feature>Feature.cs`") deja de necesitar una línea en `Program.cs`

## Contexto

Hasta el paso T3, cada `Program.cs` nombraba cada feature tres veces: `builder.Services.AddAuthFeature()`, `api.MapAuthEndpoints()` y `MapAuthInternalEndpoints()`. Una feature nueva editaba esa raíz compartida, y si alguien se olvidaba la línea, la feature compilaba y respondía 404 sin aviso. La Fase T convierte el repo en un template: lo nuevo agrega archivos aislados, no ramas en raíces compartidas.

Las features no tienen ensamblado propio: cada host compila `Features/**` dentro de su ensamblado, según su `.csproj` (ADR-0005). "Las features de este host" son entonces los tipos de ese ensamblado. La investigación está en [paso-t3-descubrimiento-de-features.md](../evolucion/investigacion/paso-t3-descubrimiento-de-features.md).

## Decisión

1. **Una interfaz de instancia en Platform.** `IFeature` (`backend/Infrastructure/Hosting/`) tiene tres métodos: `AddServices(IServiceCollection)`, `MapEndpoints(RouteGroupBuilder api)` y `MapInternalEndpoints(RouteGroupBuilder internalApi)`, este último con cuerpo vacío por defecto. Cada `<Feature>Feature` es `internal sealed class <Feature>Feature : IFeature`.
2. **Reflexión sobre el ensamblado del host.** `builder.Services.AddFeatures(typeof(<Host>).Assembly)` busca las clases que implementan `IFeature`, crea una de cada una, registra sus servicios y guarda la lista en el singleton `HostFeatures`. `app.MapFeatures()` arma el grupo versionado y el interno y mapea las rutas de cada feature. `Program.cs` no nombra ninguna feature.
3. **Orden por nombre.** Las features se registran y se mapean en orden ordinal de su nombre completo, porque `Assembly.GetTypes()` no promete un orden. Ninguna feature depende hoy del orden de otra (investigación, sección 3.2). Si alguna vez hiciera falta, va con un ADR nuevo, no con un número de orden en la interfaz.
4. **DUNE0034 cierra la falla silenciosa.** Si una compilación tiene archivos de `backend/Features/<F>/`, tiene que existir `<F>Feature`, una clase no estática ni abstracta que implementa `IFeature` y tiene un constructor público sin parámetros. Ningún otro tipo implementa `IFeature` fuera de los tests.

Descartadas:
- **Miembros `static abstract`.** Igual hacen falta reflexión y `MethodInfo` para llamarlos, y no hay `List<IFeature>` (CS8920).
- **Un source generator.** Es una pieza de build nueva, con sus tests, para una lista de cinco. Sin trimming ni AOT en el repo, no gana nada.
- **`IHostingStartup`, `ApplicationPart` y los módulos de Wolverine.** Trabajan por ensamblado o por controller, y no mapean rutas de minimal API.
- **Un chequeo al arrancar.** DUNE0034 ya ve la carpeta de una feature sin su `<F>Feature`, porque recorre todos los archivos de la compilación.

## Consecuencias

- Agregar una feature al monolito es crear `backend/Features/<F>/` con su `<F>Feature`, sin tocar archivos compartidos. Para desplegarla en otro host, se edita el `.csproj` de ese host (su `Compile` y `DuneHostFeatures`): es donde el repo decide qué despliega cada proceso.
- Los ayudantes privados de una feature dejan de ser métodos de extensión (`AddAuthIdentity(services)`), porque una clase que no es estática no puede declararlos.
- DUNE0012 deja de revisar que `Map<Feature>Endpoints` reciba el `RouteGroupBuilder` versionado: lo fija la firma de `IFeature`, y el único que llama es `MapFeatures`.
- `HostFeaturesTests` lista las features que arrancó cada host, y `FeatureHostingTests` prueba el recorrido con una feature inventada en el ensamblado de tests.
- La reflexión deja advertencias de trimming (`RequiresUnreferencedCode`) si algún día un host publica con trimming o AOT. Ese cambio necesitaría revisar esta decisión.
