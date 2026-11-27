# Molde versionado con el proyecto

Consulta: 2026-11-27. Pregunta: si cada proyecto generado lleva su copia de Dune, ¿cómo se logra que el generador de piezas (feature, comando, query…) tenga la misma versión que ese Dune? Fuentes primarias más una prueba con el SDK 10.0.401 en una carpeta temporal.

## Conclusión

`dotnet new` instala plantillas por usuario, no por proyecto. La única forma de aislarlas es una opción oculta de depuración. La vía soportada más sólida es que el generador viva dentro del repo generado y se corra con `dotnet run`. Esa es la decisión de la Fase T (`tools/Molde`).

## Plantillas por proyecto

- `dotnet new install` acepta un ID de NuGet, una carpeta o un `.nupkg` ([install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-install), [custom templates](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)). Lo instalado vale para el usuario actual y se guarda en `~/.templateengine` ([wiki](https://github.com/dotnet/templating/wiki/Inside-the-Template-Engine)).
- `--debug:custom-hive <carpeta>` aísla las plantillas instaladas, pero está definida con `Hidden = true` ([NewCommandDefinition.cs](https://github.com/dotnet/sdk/blob/main/src/Cli/Microsoft.DotNet.Cli.Definitions/Commands/New/NewCommandDefinition.cs#L46-L58)) y no figura en la [página de dotnet new](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new). La usan las pruebas de los repos de .NET ([TemplateCustomHive.cs](https://source.dot.net/Aspire.Workload.Tests/tests/Shared/WorkloadTesting/TemplateCustomHive.cs.html)). Medido: dos hives con v1 y v2 generan cada uno su versión; el `packages.json` del hive guarda la ruta absoluta, así que no se puede versionar.
- No encontré un manifiesto de herramientas locales para plantillas; lo más cercano es [dotnet/templating#1680](https://github.com/dotnet/templating/issues/1680), cerrado.

## Ejecutar sin instalar y dos versiones a la vez

- `dotnet new <nombre>` solo ve plantillas instaladas. Una instalación desde carpeta lee los archivos en el lugar (medido: un cambio en la fuente aparece sin reinstalar).
- Instalar otra versión del mismo paquete reemplaza la anterior ([install](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-install)). Medido: dos carpetas con la misma `identity` en un hive, gana la segunda y avisa del conflicto.
- [INFERENCIA, sin probar] Con otra `identity` y otro `shortName`, dos versiones convivirían.

## Qué hacen otros autores

- Jason Taylor: `ca-usecase` viaja en el mismo paquete que la solución ([nuspec](https://github.com/jasontaylordev/CleanArchitecture/blob/main/CleanArchitecture.nuspec)), toma el namespace con `msbuild:RootNamespace` y no tiene acciones posteriores ([template.json](https://github.com/jasontaylordev/CleanArchitecture/blob/main/templates/ca-use-case/.template.config/template.json)). No trata el choque de versiones.
- Ardalis: no tiene plantillas de ítem ([README](https://github.com/ardalis/CleanArchitecture)).
- Las constraints de plantillas no filtran por la versión de un paquete del proyecto ([Constraints.md](https://github.com/dotnet/templating/blob/main/docs/Constraints.md)).

## Acciones posteriores

- "Run script" corre un ejecutable en la carpeta de salida y exige `manualInstructions` ([registro](https://github.com/dotnet/templating/wiki/Post-Action-Registry)).
- `--allow-scripts` acepta `yes`, `no` o `prompt`; el valor por defecto pregunta ([TemplateCommand.cs](https://github.com/dotnet/sdk/blob/main/src/Cli/Microsoft.TemplateEngine.Cli/Commands/create/TemplateCommand.cs#L107-L116)). Medido: con `no` genera, muestra el comando y sale con 104; sin la opción y sin terminal interactiva se colgó más de 300 s.
- Con `executable: "pwsh"` funciona en Windows (medido).

## Qué aplica a este repo

- `dotnet new fst` solo crea el proyecto y no corre scripts; la inicialización la hace `tools/Molde inicializar` (decisión Q42).
- Las piezas las genera `tools/Molde`, que viaja en el mismo commit que Dune y además puede editar archivos existentes (registrar la slice en `<F>Feature.cs`), cosa que `dotnet new` no hace (decisión Q41).
- Instalar el molde para todo el usuario, como Jason Taylor, haría que proyectos de fechas distintas compartan una versión: se descarta.
