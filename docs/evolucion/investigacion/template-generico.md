# Template genérico: cómo lo hacen los profesionales (investigación)

- Fecha: 2026-11-27.
- Estado: **Listo** como investigación. No toca código. Deja abiertas tres decisiones para Tobi (al final, en "Qué aplica a este repo").
- Pregunta: ¿cómo arman los profesionales un template (= un proyecto molde del que se crean proyectos nuevos) que mantiene su arquitectura mientras crece, y que guía a personas y agentes de código a sumar features de la forma convencional?
- Decisiones ya tomadas por Tobi, que esta investigación no discute: `dotnet new` (= el comando del SDK de .NET que crea proyectos desde un template) para el proyecto y para un generador de piezas (feature, command, query, event, contract); el proyecto nuevo arranca con Auth y Users y sin features de negocio; los docs del laboratorio quedan en este repo y no van al proyecto generado; por ahora, solo backend.
- Base: rama `t3/generic-backend-template` en `ace5653`.

**Cómo leer las marcas:**

- `[FUENTE]`: lo leí en una fuente primaria (documentación oficial, repo o guía del mantenedor). Cada una tiene su link.
- `[FUENTE, resumen]`: la página era larga y la herramienta de lectura devolvió un resumen, no el texto literal. El dato es probable pero la cita no es exacta.
- `[CÓDIGO archivo:línea]`: lo leí en este repo, en `ace5653`.
- `[INFERENCIA]`: deducción mía. Nadie la dice así en una fuente.

## Respuesta corta

1. **Nadie de los grandes de .NET tiene las tres piezas juntas** (template de proyecto + generador de piezas + reglas de arquitectura que fallan el build). Jason Taylor tiene template y generador (`ca-usecase`), sin tests de arquitectura. Ardalis tiene template y reglas (referencias entre proyectos, y el analyzer NsDepCop en la variante mínima), sin generador. ABP tiene CLI y generador de CRUD, pero el generador (ABP Suite) es pago. Este repo, con Dune, ya tiene la pieza que más falta en el resto: las reglas ejecutables.
2. **`dotnet new` crea archivos, pero no edita los que ya existen.** Sus acciones posteriores (= "post actions", lo que corre después de crear) agregan referencias a un `.csproj`, proyectos a una solución, una propiedad a un JSON, o corren un script. Ninguna inserta una línea en `Program.cs` ni en `UsersFeature.cs`. En este repo, sumar una slice exige tocar `<Feature>Feature.cs` (registrar el handler y mapear el endpoint) y regenerar el código de Wolverine. Ese hueco hay que cubrirlo con otra cosa.
3. **El template se prueba creándolo en CI.** Jason Taylor corre `dotnet new install .`, crea un proyecto por cada combinación de opciones, compila y corre sus tests. Microsoft ofrece además un verificador de snapshots (= comparar la salida generada con una copia aprobada) para templates.
4. **No existe un `copier update` para `dotnet new`.** `dotnet new update` actualiza los paquetes de templates instalados, no los proyectos ya creados. Otros ecosistemas sí lo resuelven: Rails (`app:update`), Angular (`ng update` con migraciones en código), Nx (`nx migrate`), Copier y cruft (regeneran y aplican el diff).
5. **Para agentes, las fuentes coinciden en tres cosas:** un `AGENTS.md` corto que funciona como mapa; reglas mecánicas (lints y tests) en vez de más prosa, con mensajes de error que dicen cómo arreglarlo; y cuidado con el código de ejemplo, porque el agente copia lo que encuentra cerca, sea bueno o malo.
6. **La app de ejemplo de solo lectura tiene precedentes, pero el más prolijo la deja afuera del proyecto generado.** Ardalis tiene `sample/` en el repo del template y la excluye al generar. Jason Taylor deja una feature de ejemplo (TodoList) adentro y no explica cómo sacarla. ABP la ofrece como opción (`--sample-crud-page`). El riesgo que nombran las fuentes es el mismo: el agente copia patrones viejos y el código que nadie compila se desvía.
7. **Hallazgo en el código de este repo:** varias reglas de Dune nombran features fijas. `MediatorHandlerShapeAnalyzer` y `MediatorEndpointAnalyzer` solo miran `{ "Tasks", "Projects", "Users", "Auth" }` `[CÓDIGO dune/Dune.Analyzers/MediatorHandlerShapeAnalyzer.cs:12]`. Una feature nueva `Orders` quedaría fuera de esas reglas. Para un template genérico, eso es lo primero que hay que arreglar.

## Glosario corto

- **Template de proyecto** (= molde de un proyecto entero): `dotnet new mi-template -n MiApp` crea la solución completa. En `template.json` se marca con `"type": "project"` ([FUENTE: Microsoft, Custom templates](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)).
- **Template de ítem** (= molde de unos pocos archivos dentro de un proyecto que ya existe): se marca con `"type": "item"` y se corre parado en la carpeta destino ([FUENTE: Microsoft, Create an item template](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-item-template)). El generador de piezas que eligió Tobi = templates de ítem.
- **`sourceName`** (= la palabra del molde que se reemplaza por el nombre del proyecto nuevo): "The name in the source tree to replace with the name the user specifies", en contenido y en nombres de archivo ([FUENTE: dotnet/templating, Reference for template.json](https://github.com/dotnet/templating/wiki/Reference-for-template.json)). En este repo sería `TemplateApp`.
- **Golden path / paved road** (= el camino recomendado y soportado para construir algo): "the 'opinionated and supported' path to 'build something'" ([FUENTE: Spotify Engineering](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem)).
- **Reference implementation** (= app de referencia que muestra cómo se hace, sin ser un template): por ejemplo `dotnet/eShop`.

---

## 1. Templates .NET: cómo lo resuelve cada uno

### 1.1 Jason Taylor, Clean Architecture Solution Template

- **Crear el proyecto.** `dotnet new install Clean.Architecture.Solution.Template` y después `dotnet new ca-sln` ([FUENTE: README](https://github.com/jasontaylordev/CleanArchitecture)). Opciones: `--client-framework` (`angular`, `react`, `none`), `--database` (`postgresql`, `sqlite`, `sqlserver`) y `--framework` ([FUENTE: README](https://github.com/jasontaylordev/CleanArchitecture)).
- **Generar un caso de uso.** Se corre parado en `src/Application`: `dotnet new ca-usecase --name CreateTodoList --feature-name TodoLists --usecase-type command --return-type int`. Genera `CreateTodoListCommand.cs`, `CreateTodoListCommandHandler.cs` y `CreateTodoListCommandValidator.cs` en `src/Application/TodoLists/Commands/CreateTodoList/` ([FUENTE: docs, Code scaffolding](https://cleanarchitecture.jasontaylor.dev/docs/getting-started/code-scaffolding/)).
- **Cómo elige comando o query.** El `template.json` del ítem excluye `FeatureName/Queries/**/*` cuando es comando y `FeatureName/Commands/**/*` cuando es query ([FUENTE: ca-use-case/template.json](https://github.com/jasontaylordev/CleanArchitecture/blob/main/templates/ca-use-case/.template.config/template.json)). Es decir: un solo template de ítem con una opción de tipo, y exclusión de carpetas según la opción.
- **Lo que el generador no hace.** El endpoint se escribe a mano: la guía dice agregar una clase en `src/Web/Endpoints/` que implementa `IEndpointGroup`, y esa clase se registra sola ([FUENTE: docs, Code scaffolding](https://cleanarchitecture.jasontaylor.dev/docs/getting-started/code-scaffolding/)). Esquivan el problema de "editar un archivo existente" con descubrimiento automático.
- **Ejemplo incluido.** "The template includes a sample **TodoList** feature" ([FUENTE: docs, First project](https://cleanarchitecture.jasontaylor.dev/docs/getting-started/first-project/)). No encontré una opción ni pasos documentados para sacarlo.
- **Reglas de arquitectura.** No encontré tests de arquitectura ni NetArchTest/ArchUnitNET en `Directory.Packages.props`. La doc solo dice "Dependencies point inward" ([FUENTE: docs, Architecture](https://cleanarchitecture.jasontaylor.dev/docs/architecture/)).
- **Prueba del template en CI.** `.github/workflows/test-templates.yml` corre una matriz de 18 combinaciones (framework × cliente × base). Cada una hace `dotnet new install .`, `dotnet new ca-sln ... --output generated`, `dotnet build`, el build del cliente y `dotnet test` ([FUENTE: test-templates.yml](https://github.com/jasontaylordev/CleanArchitecture/blob/main/.github/workflows/test-templates.yml)).
- **`template.json` del proyecto.** `"sourceName": "CleanArchitecture"`. Excluye `.template.config/**/*`, `templates/**/*` (los templates de ítem no viajan dentro del proyecto) y archivos locales. Renombra `README-template.md` a `README.md`. Usa modificadores condicionales: con React, excluye `src/Web/ClientApp/**` y renombra `ClientApp-React` a `ClientApp` ([FUENTE: template.json](https://github.com/jasontaylordev/CleanArchitecture/blob/main/.template.config/template.json)).

### 1.2 Ardalis (Steve Smith), Clean Architecture

- **Dos templates.** `clean-arch` (Core, UseCases, Infrastructure, Web) y `min-clean` ("One Web project organized into vertical slices") ([FUENTE: docs](https://cleanarchitecture.ardalis.com/), [FUENTE: README](https://github.com/ardalis/CleanArchitecture)).
- **El ejemplo grande vive afuera del template.** El repo tiene `sample/` con `NimblePros.SampleToDo.slnx` ([FUENTE: repo, sample](https://github.com/ardalis/CleanArchitecture/tree/main/sample)), y la doc dice "It isn't part of either template" ([FUENTE: Getting started](https://cleanarchitecture.ardalis.com/getting-started/)). El `template.json` lo excluye con `"sample/**"`, junto con `docs/**` y `.github/**` ([FUENTE: template.json](https://github.com/ardalis/CleanArchitecture/blob/main/.template.config/template.json)). Esto es exactamente "los docs del laboratorio quedan en el repo y no van al proyecto".
- **El proyecto generado sí trae un ejemplo chico** (el agregado `Contributor` en el template completo) y la guía dice "delete the example code and add your own" ([FUENTE: Getting started](https://cleanarchitecture.ardalis.com/getting-started/)).
- **Sin generador de piezas.** "Neither template has template-specific options", y no hay templates de ítem ([FUENTE: Getting started](https://cleanarchitecture.ardalis.com/getting-started/)). En `min-clean`, sumar una feature es crear su carpeta, y "When you add a new feature slice (for example `OrderFeatures`), add matching rules" ([FUENTE: Dependency rules](https://cleanarchitecture.ardalis.com/dependency-rules/)).
- **Reglas.** El template completo usa referencias entre proyectos: "The compiler enforces the layering". El mínimo usa el analyzer NsDepCop, que falla con `NSDEPCOP01`. Además `TreatWarningsAsErrors` en `true` ([FUENTE: Dependency rules](https://cleanarchitecture.ardalis.com/dependency-rules/), [FUENTE: Testing](https://cleanarchitecture.ardalis.com/testing/)).
- **Prueba en CI.** Compilan y prueban la solución del template y, en otro job, la del `sample/` ([FUENTE: cross-platform-build-test.yml](https://github.com/ardalis/CleanArchitecture/blob/main/.github/workflows/cross-platform-build-test.yml)). La prueba que instancia el template con `dotnet new` está **comentada** en `publish-templates.yml` ([FUENTE: publish-templates.yml](https://github.com/ardalis/CleanArchitecture/blob/main/.github/workflows/publish-templates.yml)). O sea: no prueban lo que el usuario recibe, solo el repo fuente.

### 1.3 ABP Framework

- **Crear.** `abp new <solución>` con `-t` = `empty`, `app`, `app-nolayers` o `microservice` ([FUENTE, resumen: ABP CLI](https://abp.io/docs/latest/cli)).
- **Módulos.** `abp new-module <nombre>` crea un módulo (por defecto `module:ddd`) y lo puede sumar a una solución con `--target-solution` ([FUENTE, resumen: ABP CLI](https://abp.io/docs/latest/cli)). Cada módulo es una clase que hereda de `AbpModule` y declara sus dependencias con `[DependsOn(...)]` ([FUENTE, resumen: Modularity basics](https://abp.io/docs/latest/framework/architecture/modularity/basics)).
- **Ejemplo opcional.** `--sample-crud-page` agrega el ejemplo BookStore ([FUENTE, resumen: ABP CLI](https://abp.io/docs/latest/cli)). Es una opción de creación, no una carpeta que después se borra.
- **Generador de CRUD.** ABP Suite genera páginas CRUD para MVC, Blazor, Angular y React ([FUENTE, resumen: ABP Suite](https://abp.io/docs/latest/suite)). Es pago: "You must have an ABP Team or a higher license to use the ABP Suite" ([FUENTE, resumen: ABP Suite](https://abp.io/docs/latest/suite)).
- **Reglas.** Guías escritas de capas: "HTTP API package only depends on the Application.Contracts package" ([FUENTE, resumen: Module architecture best practices](https://abp.io/docs/latest/framework/architecture/best-practices/module-architecture)). No encontré analyzers ni tests de arquitectura.
- **Actualizar.** `abp update` actualiza versiones de paquetes NuGet y npm ([FUENTE, resumen: ABP CLI](https://abp.io/docs/latest/cli)). No encontré nada que vuelva a aplicar cambios del template a una solución existente.
- **`abp generate-proxy`** genera clientes (`csharp`, `ng`, `js`) a partir de la API del servidor ([FUENTE, resumen: ABP CLI](https://abp.io/docs/latest/cli)). No aplica: Tobi dejó el frontend afuera por ahora.

### 1.4 Modular monolith y vertical slices (repos de referencia, no templates)

- **Kamil Grzybek, modular-monolith-with-ddd.** Es una app de ejemplo, no un template: "Full Modular Monolith .NET application with Domain-Driven Design approach" ([FUENTE: README](https://github.com/kgrzybek/modular-monolith-with-ddd)). Sus reglas se parecen a las de este repo: "Each Module has it's own data in a separate schema", "Modules can only have a dependency on the integration events assembly of other Module" ([FUENTE: README](https://raw.githubusercontent.com/kgrzybek/modular-monolith-with-ddd/master/README.md)). Las hace cumplir con tests de arquitectura sobre NetArchTest ([FUENTE, resumen: README, sección Architecture Unit Tests](https://raw.githubusercontent.com/kgrzybek/modular-monolith-with-ddd/master/README.md)). No tiene generador de módulos.
- **Milan Jovanović.** Ofrece templates gratis "you can clone, rename, and build on", que llegan por email, sin `dotnet new` ([FUENTE: Templates](https://milanjovanovic.tech/templates)). En su template de vertical slices, sumar una feature es copiar: "Open Features/Todos/CreateTodo.cs, copy it, rename it. That is what adding a feature looks like." ([FUENTE: VSA template](https://milanjovanovic.tech/templates/vertical-slice-architecture)). Su template de Clean Architecture tiene "the dependency rule enforced by architecture tests, so a wrong reference fails the build" ([FUENTE: Templates](https://milanjovanovic.tech/templates)). Sobre la forma: "Modular Monolith answers the macro question ... Vertical Slice Architecture answers the micro question" ([FUENTE: blog](https://www.milanjovanovic.tech/blog/where-vertical-slices-fit-inside-the-modular-monolith-architecture)).
- **Jimmy Bogard.** La regla de VSA: "Minimize coupling between slices, and maximize coupling in a slice" ([FUENTE: Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/)). Su `ContosoUniversityDotNetCore-Pages` es "Contoso University, the way I would write it": un ejemplo, no un template ([FUENTE: README](https://github.com/jbogard/ContosoUniversityDotNetCore-Pages)).
- **dotnet/eShop.** "A reference .NET application implementing an e-commerce website using a services-based architecture with Aspire" ([FUENTE: README](https://github.com/dotnet/eShop)). Es la referencia oficial de Microsoft y vive en su propio repo, no adentro de un template.
- **Wolverine.** No tiene template de `dotnet new`: su guía arranca con `dotnet new webapi` y `dotnet add package WolverineFx` ([FUENTE: Getting started](https://wolverinefx.net/introduction/getting-started)). Para monolitos modulares recomienda "separate `DbContext` types for different modules that all target a separate database schema", "asynchronous communication between modules", y nunca llamar `IMessageBus.InvokeAsync()` desde un handler a otro ([FUENTE: Modular Monoliths](https://wolverinefx.net/tutorials/modular-monolith)). Este repo ya sigue las tres.
- **JasperFx (la empresa de Wolverine) vende skills para agentes**: "Teach your coding assistant the Critter Stack", "Designed for agents, not humans", "tuned for Claude Code". Incluye "Bootstrap a new Wolverine + Marten application" con "a vertical-slice example". Es pago y propietario ([FUENTE: JasperFx AI Skills](https://jasperfx-ai-skills.pages.dev/), [FUENTE: skill](https://jasperfx-ai-skills.pages.dev/skills/critterstack-arch-new-project-wolverine-marten)). Es un precedente de "el generador del camino recomendado es una skill para el agente", no un template.
- **Librerías de tests de arquitectura.** NetArchTest: "A fluent API for .Net that can enforce architectural rules in unit tests" ([FUENTE](https://github.com/BenMorris/NetArchTest)). ArchUnitNET: "A C# architecture test library to specify and assert architecture rules" ([FUENTE](https://github.com/TNG/ArchUnitNET)). Dune usa analyzers Roslyn, que fallan al compilar, antes que un test.

### 1.5 Tabla comparativa

| | Crea proyecto | Genera piezas | Ejemplo en el proyecto generado | Reglas que fallan | Prueba el template generado en CI |
|---|---|---|---|---|---|
| Jason Taylor | `dotnet new ca-sln` | `dotnet new ca-usecase` (ítem) | Sí (TodoList), sin pasos para sacarlo | No encontré | Sí, matriz de 18 |
| Ardalis | `dotnet new clean-arch` / `min-clean` | No | Chico (`Contributor`); el grande queda en `sample/`, excluido | Referencias entre proyectos, NsDepCop | No (comentado) |
| ABP | `abp new` | `abp new-module`; CRUD con Suite (pago) | Opcional (`--sample-crud-page`) | Guías escritas | No encontré |
| Milan | Clonar | Copiar y renombrar | Sí (Todos, Users) | Tests de arquitectura (Clean) | No encontré |
| kgrzybek, eShop, Bogard | Repo de ejemplo | No | Es todo ejemplo | NetArchTest (kgrzybek) | No aplica |
| Este repo hoy | No | No | No aplica | Dune: analyzers + gates | No |

---

## 2. Datos de `dotnet new` que importan para este repo

### 2.1 Nombre, opciones y archivos

- **`sourceName`** reemplaza el texto en contenido y nombres de archivo; si el usuario no da `-n`, usa el nombre de la carpeta ([FUENTE: Custom templates](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)). Con `preferNameDirectory: true` crea una carpeta con ese nombre ([misma fuente](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)).
- **Símbolos** (= las variables del template): tipos "Parameter, Derived, Computed, Generated, Bind" ([FUENTE: Reference for template.json](https://github.com/dotnet/templating/wiki/Reference-for-template.json)). Un símbolo `derived` toma otro valor y lo transforma (por ejemplo, de `Orders` sacar el singular o el namespace), y `fileRename` aplica el resultado a los nombres de archivo ([misma fuente](https://github.com/dotnet/templating/wiki/Reference-for-template.json)). Ardalis usa `"valueTransform": "safe_namespace"` para que el nombre sea un namespace válido ([FUENTE: template.json de Ardalis](https://github.com/ardalis/CleanArchitecture/blob/main/.template.config/template.json)).
- **Contenido condicional.** En C#: `#if (opcion)` ... `#endif`. En `.csproj`/XML: `<!--#if (A && B) -->`. En JSON: `//#if (opcion)` ([FUENTE: Conditional processing](https://github.com/dotnet/templating/wiki/Conditional-processing-and-comment-syntax)). Las condiciones usan sintaxis de preprocesador de C++ con `&&`, `||`, `==`, `!=`, en una sola línea ([FUENTE: Conditions](https://github.com/dotnet/templating/wiki/Conditions)).
- **Excluir archivos.** `sources[].exclude` tiene por defecto `**/[Bb]in/**`, `**/[Oo]bj/**`, `.template.config/**/*`, `**/*.filelist`, `**/*.user`, `**/*.lock.json` ([FUENTE: Reference for template.json](https://github.com/dotnet/templating/wiki/Reference-for-template.json)). Si se define a mano, reemplaza ese valor [INFERENCIA: por eso Jason Taylor y Ardalis repiten los valores por defecto en su lista]. `sources[].modifiers[]` agrega `include`/`exclude`/`rename` según una `condition` ([misma fuente](https://github.com/dotnet/templating/wiki/Reference-for-template.json)).
- **Dotfiles.** Al empaquetar, `NoDefaultExcludes` en `true` mantiene archivos como `.gitignore` en el paquete ([FUENTE: Create a template package](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package)). Este repo tiene `.agents/`, `.github/` y `.mcp.json`: sin eso, se pierden [INFERENCIA].

### 2.2 Templates de ítem y sus límites

- Un ítem se instala igual que un proyecto y se corre en la carpeta destino; `-o` elige otra carpeta ([FUENTE: Create an item template](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-item-template)).
- Las **acciones posteriores** registradas son: restaurar NuGet, correr un script, abrir un archivo en el editor, agregar una referencia a un `.csproj`, agregar proyectos a una solución, agregar una propiedad a un JSON existente, cambiar permisos (Unix) y mostrar instrucciones manuales ([FUENTE: Post Action Registry](https://github.com/dotnet/templating/wiki/Post-Action-Registry)). **Ninguna edita un archivo C# existente.**
- "Run script" corre un ejecutable con la carpeta de salida como directorio de trabajo ([FUENTE: Post Action Registry](https://github.com/dotnet/templating/wiki/Post-Action-Registry)). Los tests de Microsoft pasan `--allow-scripts yes` para que corra ([FUENTE: PostActionTests](https://source.dot.net/dotnet-new.IntegrationTests/PostActionTests.Approval.cs.html), [FUENTE: PostActionDispatcher](https://source.dot.net/Microsoft.TemplateEngine.Cli/PostActionDispatcher.cs.html)). [INFERENCIA: sin ese flag, `dotnet new` pregunta antes de correrlo; no encontré la página de Learn que lo documente.]
- Microsoft avisa: "Templates can run MSBuild tasks and arbitrary code during project creation. Only install templates from sources you trust." ([FUENTE: Custom templates](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)).

### 2.3 Empaquetar y probar

- **Paquete.** Un `.csproj` con `PackageType` = `Template`, `IncludeContentInPack` = `true`, `IncludeBuildOutput` = `false`. Se instala con `dotnet new install <paquete o carpeta>` ([FUENTE: Custom templates](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)). Hay un template para arrancar: `dotnet new templatepack` ([misma fuente](https://learn.microsoft.com/en-us/dotnet/core/tools/custom-templates)).
- **Prueba por snapshot.** `Microsoft.TemplateEngine.Authoring.TemplateVerifier` corre el template y compara la salida con una copia aprobada, usando Verify; sirve con xUnit o MSTest ([FUENTE: Templates Testing Tooling](https://github.com/dotnet/templating/blob/main/docs/authoring-tools/Templates-Testing-Tooling.md)). Hay también una CLI: `dotnet-template-authoring verify <short-name>` ([misma fuente](https://github.com/dotnet/templating/blob/main/docs/authoring-tools/Templates-Testing-Tooling.md)). El SDK de .NET la usa para sus propios templates ([FUENTE: DotnetClassTemplateTests](https://source.dot.net/dotnet-new.IntegrationTests/DotnetClassTemplateTests.cs.html)).
- **Validación de `template.json`.** La tarea `Validate` y el comando `validate` figuran como "planned" ([FUENTE: Authoring Tools](https://github.com/dotnet/templating/blob/main/docs/authoring-tools/Authoring-Tools.md)). Hoy no existen.
- **Prueba de humo** (= crear, compilar y correr tests): es lo que hace Jason Taylor (sección 1.1).

### 2.4 Actualizar proyectos ya creados

- `dotnet new update` "updates installed template packages" ([FUENTE: dotnet new update](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-update)). La página no habla de proyectos creados. [INFERENCIA: un proyecto generado no recibe cambios posteriores del template. No encontré ninguna herramienta oficial de .NET que lo haga.]

---

## 3. Otros ecosistemas: qué vale copiar

### 3.1 Generadores propios del equipo

- **Rails.** Los generadores propios viven en `lib/generators`. Cada proyecto puede cambiar la salida de los generadores de Rails: "When resolving generator template files, Rails looks first in the application's `lib/templates/` directory" ([FUENTE: Rails generators](https://guides.rubyonrails.org/generators.html)).
- **Angular.** "A schematic is a template-based code generator that supports complex logic" ([FUENTE: Schematics](https://angular.dev/tools/cli/schematics)). Trabaja sobre un sistema de archivos virtual: "creating a virtual file system", y solo escribe si todo sale bien ([FUENTE: Authoring schematics](https://angular.dev/tools/cli/schematics-authoring)). Un schematic **sí** puede editar archivos existentes. Este repo ya aplica la idea en el frontend: `angular.json` fija `"style": "none"` para que `ng generate component` produzca código que el gate acepta, y FRONT014 lo verifica ([CÓDIGO docs/conventions.md, sección "Nombres y estilo", última regla]).
- **Nx.** "generators help you standardize these tasks in a consistent, and predictable manner"; el generador es una función que manipula "a tree that represents the file system" ([FUENTE: Nx local generators](https://nx.dev/docs/kb/local-generators)). Los límites entre módulos se hacen cumplir con tags y la regla `@nx/enforce-module-boundaries`; "Projects without any tags cannot depend on any other projects" ([FUENTE: Enforce module boundaries](https://nx.dev/docs/features/enforce-module-boundaries)).
- **Backstage.** Un template es "a small `yaml` definition" con parámetros (un formulario) y pasos ([FUENTE: Writing templates](https://backstage.io/docs/features/software-templates/writing-templates)). Spotify lo usa para su golden path, que no prohíbe salirse: "you can of course leave the Golden Path and do your own thing", pero sin el mismo soporte ([FUENTE: Spotify](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem)). Spotify suma el "Golden State": "a list of checks that engineers can use to know if their systems are following the Golden Path" ([misma fuente](https://engineering.atspotify.com/2020/08/how-we-use-golden-paths-to-solve-fragmentation-in-our-software-ecosystem)). Golden State = Dune.

### 3.2 Recibir actualizaciones del template

| Herramienta | Cómo actualiza un proyecto existente | Fuente |
|---|---|---|
| Rails | `bin/rails app:update` regenera archivos de configuración y pregunta en cada conflicto (`Overwrite ... [Ynaqdh]`) | [Upgrading Rails](https://guides.rubyonrails.org/upgrading_ruby_on_rails.html) |
| Angular | `ng update` corre "update schematics": código que ajusta el proyecto a los cambios incompatibles | [Schematics](https://angular.dev/tools/cli/schematics) |
| Nx | `nx migrate` escribe `migrations.json` ("No source code is touched yet") y `--run-migrations` las aplica; "Each Nx plugin can provide migrations" | [Automate updating dependencies](https://nx.dev/docs/features/automate-updating-dependencies) |
| Copier | `copier update` "regenerates a fresh project from the current template version", saca el diff con el proyecto y lo aplica; marca conflictos estilo git. Necesita `.copier-answers.yml` y el template versionado con tags | [Copier, Updating](https://copier.readthedocs.io/en/stable/updating/) |
| cruft | `.cruft.json` guarda el hash del template y las respuestas; `cruft update` muestra los cambios antes de aplicarlos; `cruft check` falla en CI si el proyecto está atrasado | [cruft](https://cruft.github.io/cruft/) |
| `dotnet new` | No tiene | [dotnet new update](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-update) |

[INFERENCIA] Dos caminos para .NET: (a) mover lo que debe actualizarse a paquetes versionados (es lo que hace ABP con `abp update`, y lo que Nx hace con migraciones por plugin); (b) usar Copier sobre el repo, que no depende del lenguaje, con el costo de una segunda herramienta.

---

## 4. Cómo se guía a los agentes de código

### 4.1 Dónde van las instrucciones

- `AGENTS.md` va en la raíz; en monorepos, uno por subproyecto: "the closest one takes precedence" ([FUENTE: agents.md](https://agents.md/)). GitHub Copilot lo respeta igual: "the nearest `AGENTS.md` file in the directory tree will take precedence" ([FUENTE: GitHub Docs](https://docs.github.com/en/copilot/customizing-copilot/adding-repository-custom-instructions-for-github-copilot)).
- OpenAI usa un `AGENTS.md` corto, "(roughly 100 lines)", como mapa, y un `docs/` "treated as the system of record" ([FUENTE: OpenAI, Harness engineering](https://openai.com/index/harness-engineering/)). Probaron el archivo grande y falló: "Agents end up pattern-matching locally instead of navigating intentionally" ([misma fuente](https://openai.com/index/harness-engineering/)).
- Claude Code: cada línea del archivo de instrucciones "takes a little of Claude's attention on every task"; "Only write down what Claude can't find by reading the repo" ([FUENTE: Claude Code best practices](https://code.claude.com/docs/en/best-practices)).

### 4.2 Reglas mecánicas antes que prosa

- "Documentation alone doesn't keep a fully agent-generated codebase coherent." Las reglas se hacen cumplir "via custom linters ... and structural tests", y "we write the error messages to inject remediation instructions into agent context" ([FUENTE: OpenAI](https://openai.com/index/harness-engineering/)). Es decir: el mensaje de error dice qué hacer.
- "If you keep adding emphasis to a CLAUDE.md line and Claude still misses it, that's a sign the line belongs in a hook" ([FUENTE: Claude Code best practices](https://code.claude.com/docs/en/best-practices)).
- pstack (Lauren Tan, poteto): primero la estructura y los tipos, después un lint "whose error names the file, type, or function to use instead", y la prosa al final, "only for judgment calls", porque "Nothing fails when an agent skips them" ([FUENTE: pstack /correct](https://github.com/cursor/plugins/blob/main/pstack/skills/correct/SKILL.md)). También propone "a table in the agent instruction file that pairs each rule with what enforces it" ([misma fuente](https://github.com/cursor/plugins/blob/main/pstack/skills/correct/SKILL.md)) y el principio `encode-lessons-in-structure`: "Encode the rule as a lint, metadata flag, runtime check, or script instead of more text" ([FUENTE: pstack README](https://github.com/cursor/plugins/tree/main/pstack)).

### 4.3 Ejemplos y el riesgo de copiar

- Apuntar a un ejemplo funciona: "follow the pattern in HotDogWidget.php and build a calendar widget" es mejor pedido que uno genérico ([FUENTE: Claude Code best practices](https://code.claude.com/docs/en/best-practices)).
- Pero el agente copia todo lo que encuentra: "Codex replicates patterns that already exist in the repository—even uneven or suboptimal ones. Over time, this inevitably leads to drift." ([FUENTE: OpenAI](https://openai.com/index/harness-engineering/)).
- pstack lo pone como supuesto de diseño: el agente "sees only the files it opened, copies the nearest example, and takes the shortest path that compiles", así que "Design the repo so a change that looks right from one file is right for the whole repo" y "Delete old ways and dead code an agent would copy" ([FUENTE: pstack /correct](https://github.com/cursor/plugins/blob/main/pstack/skills/correct/SKILL.md)).
- Claude Code muestra cómo avisarlo cuando no se puede borrar: "src/legacy/ is still live but don't copy its patterns into new code" ([FUENTE: Claude Code best practices](https://code.claude.com/docs/en/best-practices)).

---

## 5. La app de ejemplo de solo lectura dentro del proyecto generado

### 5.1 Precedentes

- **Ejemplo afuera del template, en el repo fuente:** Ardalis (`sample/`, excluido con `sources.exclude`), compilado y probado en el CI del repo ([sección 1.2](#12-ardalis-steve-smith-clean-architecture)).
- **Ejemplo adentro, compilado, para borrar a mano:** Ardalis (`Contributor`, "delete the example code"), Jason Taylor (TodoList, sin pasos), Milan (Todos, que además es el molde: "copy it, rename it").
- **Ejemplo opcional al crear:** ABP (`--sample-crud-page`).
- **App de referencia en su propio repo:** eShop, kgrzybek, Contoso de Bogard.
- **Skill para el agente:** JasperFx vende skills con "a vertical-slice example".
- **No encontré** ningún template que incluya un ejemplo **excluido del build** (código que viaja pero no compila) para que los agentes lo lean.

### 5.2 Riesgos

1. **Desvío** (= el ejemplo deja de coincidir con las reglas). Si el ejemplo no compila, Dune no lo revisa: una regla nueva lo deja atrás sin que nada falle [INFERENCIA]. OpenAI lo nombra como el resultado inevitable de copiar patrones dispares ([FUENTE](https://openai.com/index/harness-engineering/)).
2. **El agente copia el dominio del ejemplo.** Con "copies the nearest example" ([FUENTE: pstack](https://github.com/cursor/plugins/blob/main/pstack/skills/correct/SKILL.md)), un agente que crea `Orders` puede arrastrar nombres, permisos o eventos de `Tasks` [INFERENCIA].
3. **Ruido para las reglas por nombre.** Si el ejemplo vive en `backend/Features/`, los gates y analyzers lo ven como feature real [INFERENCIA].
4. **Nadie lo borra.** Tobi propone quitarlo "cuando el proyecto tenga sus features"; ningún paso lo dispara [INFERENCIA]. Jason Taylor no documenta cómo sacar TodoList ([FUENTE](https://cleanarchitecture.jasontaylor.dev/docs/getting-started/first-project/)).

### 5.3 Mitigaciones que salen de las fuentes

- **Que el ejemplo compile y pase los gates**, como el `sample/` de Ardalis en su CI. Si no compila, no es evidencia.
- **Que el generador sea el camino, no el ejemplo** (Jason Taylor, Rails, Nx, Angular): el agente corre `dotnet new <pieza>` y no necesita copiar.
- **Que las reglas fallen con un mensaje que diga cómo arreglar** (OpenAI, pstack). Así, aunque el agente copie mal, el build lo corrige.
- **Que el ejemplo sea opcional y se elija al crear** (ABP), o que viva fuera del proyecto generado (Ardalis, eShop).
- **Si queda, que `AGENTS.md` diga qué no copiar** (el `src/legacy/` de Claude Code).

---

## Qué aplica a este repo

Contraste con [ADR-0004](../../adr/0004-vertical-slices-deep-modules.md), [architecture.md](../../architecture.md) y [conventions.md](../../conventions.md).

### Lo que ya está a favor

- **Las reglas ejecutables ya existen.** ADR-0004 dice que "Dune hace cumplir los límites" (punto 7), y conventions.md exige que cada regla tenga una fixture válida y una violación deliberada. Eso es el "Golden State" de Spotify y los "custom linters" de OpenAI. Ninguno de los templates .NET revisados tiene algo tan completo.
- **Auth y Users ya son el ejemplo vivo.** Según ADR-0004 (puntos 2, 4 y 9), las dos tienen slices como handlers de Wolverine, contratos en las dos direcciones, `I<Feature>Module`, `Domain/`, `Persistence/` y carpetas técnicas. Van a estar en el proyecto generado, compiladas y revisadas por Dune [INFERENCIA: cumplen el rol de la app de ejemplo sin los riesgos 1 y 3 de la sección 5.2].
- **`sourceName` encaja directo.** El namespace raíz es `TemplateApp` y DUNE0005 exige que coincida con la ruta (conventions.md, "Nombres y estilo"). Los archivos también lo usan: `TemplateApp.slnx`, `TemplateApp.csproj`. Con `"sourceName": "TemplateApp"`, `dotnet new` renombra los dos [INFERENCIA: falta probar que no rompa textos donde `TemplateApp` no es el nombre del proyecto].
- **El principio "el generador produce lo que el gate acepta" ya está en el frontend** (FRONT014, conventions.md). Hay que llevarlo al backend.

### Lo que choca o falta

1. **Dune nombra features fijas.** `MediatorHandlerShapeAnalyzer` y `MediatorEndpointAnalyzer` solo miran `{ "Tasks", "Projects", "Users", "Auth" }` `[CÓDIGO dune/Dune.Analyzers/MediatorHandlerShapeAnalyzer.cs:12, MediatorEndpointAnalyzer.cs:15]`; `TransactionOwnerAnalyzer` mira `{ "Auth", "Users", "Projects" }` `[CÓDIGO dune/Dune.Analyzers/TransactionOwnerAnalyzer.cs:17]`; `HostBoundaryAnalyzer` y `IdempotentPostAnalyzer` nombran `Tasks` y `Auth` `[CÓDIGO dune/Dune.Analyzers/HostBoundaryAnalyzer.cs:22, IdempotentPostAnalyzer.cs:12]`; `check-codegen.mjs` y `check-host-items.mjs` nombran los hosts y Tasks `[CÓDIGO dune/gates/check-codegen.mjs:6]`. Entonces una feature nueva `Orders` no queda cubierta por las reglas del mediator. Para el template, cada regla tiene que valer para cualquier feature bajo `backend/Features/`, con su fixture y su violación (regla 4 de AGENTS.md).
2. **Sumar una slice toca archivos que `dotnet new` no edita.** Hoy una slice nueva exige: los archivos de la slice; `services.AddMediatorHandler<...>(MediatorTransaction.X)` y `XEndpoint.Map(...)` en `<Feature>Feature.cs` `[CÓDIGO backend/Features/Users/UsersFeature.cs:40-64]`; y regenerar el código de Wolverine, que `check-codegen.mjs` exige versionado `[CÓDIGO dune/gates/check-codegen.mjs:6-20]`. Una feature nueva además se lista en `Program.cs` (conventions.md: "`Program.cs` lista las features de forma explícita"). Opciones:
   - **Templates de ítem + reglas que detectan lo que falta.** El ítem crea los archivos; una regla de Dune falla si una slice tiene handler o endpoint sin registrar, con un mensaje que dice la línea exacta a agregar. Gana: un solo mecanismo (Dune), sin scripts. Pierde: el agente necesita una vuelta más de build.
   - **Templates de ítem + post action "Run script".** El script (Node, como los gates) inserta las líneas. Gana: un comando y listo. Pierde: `--allow-scripts`, y un script que edita C# por texto.
   - **Descubrimiento automático** (lo que hace Jason Taylor con `IEndpointGroup`). Gana: no hay nada que registrar. Pierde: choca con conventions.md ("`Program.cs` lista las features de forma explícita") y con el registro explícito de handlers que eligió el paso 29.1 (ADR-0004 punto 9, "la indirección se acepta a propósito"). Habría que cambiar un ADR.
   - Recomendación [INFERENCIA]: la primera. Sigue la regla 10 de AGENTS.md (los docs deciden y Dune los sigue) y lo que piden OpenAI y pstack (el error dice cómo arreglarlo). La industria se reparte: Jason Taylor usa descubrimiento automático; Angular y Nx editan archivos desde el generador.
3. **Los docs de diseño hablan de pasos y de Tasks.** architecture.md usa `Features/Tasks/` como árbol de ejemplo y cuenta la historia por paso ("Desde el paso 29.1…"); ADR-0004 tiene una sección "Cambios" con fechas del laboratorio. Si viajan así, el agente del proyecto nuevo lee una historia que no es la suya y un dominio (Tasks) que no existe ahí. Si no viajan, Dune queda sin su fuente de verdad (regla 10). [INFERENCIA: el proyecto generado necesita su propia versión de `architecture.md` y `conventions.md`, escrita sobre Auth y Users, sin historia; esa versión es contenido del template y se prueba como el código.]
4. **Prueba del template en CI.** Falta una prueba que haga lo de Jason Taylor: `dotnet new install .`, crear el proyecto, compilar con Dune, correr los tests. Y una más, propia de este repo: crear una feature con los templates de ítem dentro del proyecto generado y verificar que pasa todos los gates. Esa segunda prueba es la que demuestra que "un agente que corre el generador produce una feature que sigue la metodología".
5. **Actualizaciones.** `dotnet new` no las trae. Si Dune va como código adentro del proyecto generado, cada proyecto se queda con la versión de Dune del día que nació [INFERENCIA]. Si Dune va como paquete NuGet (analyzers) más un paquete de gates, un proyecto recibe reglas nuevas subiendo la versión, como `abp update` o las migraciones de plugins de Nx.

### Sobre la app de ejemplo de solo lectura

[INFERENCIA, con las fuentes de la sección 5] No encontré precedente de código que viaje excluido del build. Los precedentes compilan su ejemplo o lo dejan fuera del proyecto. Con Auth y Users en el proyecto generado, el ejemplo ya existe y lo revisa Dune. Las opciones, con lo que gana y pierde Tobi:

- **Sin app de ejemplo; Auth y Users como referencia + generador.** Gana: cero desvío, nada que borrar. Pierde: no hay un ejemplo de feature de negocio con proyección, saga o evento entre features.
- **Ejemplo opcional al crear (`dotnet new ... --example`), compilado y con Dune** (como ABP). Gana: el ejemplo nunca se desvía porque falla el build. Pierde: hay que mantener el ejemplo en el template y sacarlo a mano después (como Ardalis).
- **Ejemplo de solo lectura excluido del build** (la propuesta). Gana: el agente ve una feature completa sin que afecte al proyecto. Pierde: nadie verifica que siga las reglas, y el agente puede copiar su dominio.
- **Ejemplo afuera: link a este repo en una versión fija** (como eShop o el `sample/` de Ardalis). Gana: el ejemplo vive donde se prueba. Pierde: el agente necesita salir del proyecto para leerlo.

Recomendación: la primera o la segunda. La tercera es la única sin precedente y la que más choca con "copies the nearest example".

### Decisiones para Tobi

1. Cómo se registra una slice nueva: regla de Dune que guía, script, o descubrimiento automático.
2. La app de ejemplo: ninguna, opcional y compilada, de solo lectura, o afuera.
3. Cómo llega Dune al proyecto generado: como código copiado o como paquete versionado. Es una elección de herramienta (regla 5).

## Lo que no comprobé

- No corrí `dotnet new` ni armé un `template.json` contra este repo.
- No leí el texto literal de las páginas de ABP ni la sección de tests de arquitectura de kgrzybek: son resúmenes (marcados).
- No encontré en Learn la página que documenta `--allow-scripts`; lo saqué de los tests y del código del SDK.
- No leí las guías 01 a 10 de pstack ni la sección completa de Nx sobre editar archivos existentes desde un generador.
- No revisé qué textos del repo contienen `TemplateApp` fuera de namespaces y nombres de proyecto.
