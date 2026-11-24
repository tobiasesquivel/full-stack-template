# Informe: componentes con estado y componentes de presentación en Angular (v20-v22)

**Alcance**: solo investigación. Leí fuentes web, el `llms-full.txt` de angular.dev, las referencias de la skill oficial `angular-developer`, los tipos de `@angular/core` 22.2.1 instalados, el schematic de componentes del CLI, el frontend del repo y el gate `dune/check-frontend.mjs` con sus tests. No cambié código.

**Distinción de fuentes**: **[PRIMARIA]** = el equipo de Angular (angular.dev, los repos `angular/*`, el código instalado). **[OPINIÓN]** = practicante que interpreta. **[INFERENCE]** = razonamiento mío. **[REPO]** = observado en el código, con `archivo:línea`.

**Términos**: uso un solo nombre por idea.
- **Componente de ruta**: el destino de una navegación. Es el término de angular.dev (*routed component*). Otros lo llaman *smart*, *container*, *page* o *feature component*.
- **Componente de presentación**: recibe datos con `input()`, avisa con `output()` y no inyecta servicios de la app. Otros lo llaman *dumb*, *presentational* o *ui component*.
- **Servicio de la app**: una clase del repo con `@Service()` o `@Injectable()`, como `TasksApi` o `Session`.

---

## Conclusión

1. **La división sigue vigente como práctica, pero Angular no la nombra.** angular.dev no usa "smart", "dumb", "container" ni "presentational" en ninguna guía: lo busqué en el `llms-full.txt` completo (856 KB). La guía de estilo pide algo más general: *"Keep components and directives focused on presentation"* y llevar a otros archivos el código que no depende de la UI. [PRIMARIA]
2. **Los signals no eliminan la división: la abaratan.** El estado local de la UI ya no exige un componente padre: un `signal()` dentro del hijo alcanza. El componente de ruta queda para orquestar: leer datos, llamar a la API y decidir. [OPINIÓN + INFERENCE]
3. **OnPush es el valor por defecto en v22.** Lo verifiqué en `@angular/core` 22.2.1: *"NOTE: OnPush is enabled by default"*, y el antiguo `Default` pasó a llamarse `Eager`. La app además es zoneless (no carga `zone.js`). Con OnPush, un componente de presentación con `input()` solo se vuelve a revisar cuando cambia su entrada. [PRIMARIA]
4. **El nombre correcto en este repo es "componente de ruta".** Es el término oficial y el gate lo puede detectar: es lo que carga `loadComponent` en `<feature>.routes.ts`. No hace falta una carpeta `containers/` ni `components/`: la guía de estilo y `conventions.md:31` las prohíben. [PRIMARIA + REPO]
5. **`Tasks` es el único componente que conviene partir.** Tiene 198 líneas de lógica y 158 de template, con 12 signals de estado: lista, alta, edición en línea, diálogo de borrado y paginación. `Login`, `Register` y `Profile` son de ruta, chicos y de un solo propósito: partirlos agrega ceremonia sin beneficio. [REPO + INFERENCE]
6. **Propongo partir `Tasks` en tres piezas de presentación, en tres pasos:** `TaskPager`, `TaskRow` y `TaskDeleteDialog`. El formulario de alta queda en `Tasks`. Su envío es asíncrono y necesita el resultado de la API. Un `output()` no devuelve resultados. [INFERENCE]
7. **Una regla nueva puede hacer cumplir el límite: FRONT013.** Un componente que no es de ruta no inyecta servicios de la app ni el `Router`. La regla mira el tipo que devuelve cada `inject(...)`, así un alias no la esquiva. [INFERENCE]

---

## 1. Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [Guía de estilo](https://angular.dev/style-guide) | PRIMARIA | *"Keep components and directives focused on presentation."* *"For code that makes sense on its own, decoupled from the UI, prefer refactoring to other files."* Organiza por feature y evita carpetas `components`, `directives` y `services`. *"Prefer `protected` access for any members that are meant to be read from the component's template."* |
| `llms-full.txt` de angular.dev (descargado el 2026-10-17) | PRIMARIA | No contiene "presentational", "smart component" ni "dumb". Sí define *routed component*: *"A routed component is the destination of a Router navigation."* |
| [best-practices.md](https://angular.dev/assets/context/best-practices.md) y [AGENTS.md de Angular](https://angular.dev/assets/context/AGENTS.md) | PRIMARIA | *"Keep components small and focused on a single responsibility."* `input()` y `output()` en lugar de decoradores. `model()` para el enlace en dos sentidos. `computed()` para lo derivado y `linkedSignal()` para lo que depende de varias fuentes. No escribir `OnPush`: ya es el valor por defecto. |
| `inputs.md`, `outputs.md`, `queries-and-projection.md` de la skill | PRIMARIA | `input.required()` da error de compilación si falta la entrada. Los outputs se nombran sin `on` (`valueChanged`). `viewChild` devuelve un signal. |
| `naming-conventions.md` de la skill | PRIMARIA | Componente principal con el nombre de la feature (`features/profile/profile.ts`). Subcomponentes con su rol visual: `features/profile/components/profile-header.ts`. Componentes de presentación compartidos en `shared/components/`. |
| [Testing scenarios](https://angular.dev/guide/testing/components-scenarios) | PRIMARIA | Prueba un componente con entradas y salidas con `setInput`, un host de prueba o `inputBinding`/`outputBinding`. Un servicio se reemplaza por un doble, *"sparingly"*. |
| `@angular/core` 22.2.1, `ChangeDetectionStrategy` | PRIMARIA | `OnPush = 0` con *"OnPush is enabled by default"*; `Eager = 1`; `Default` está `@deprecated`. |
| [Ninja Squad, Angular 22.0](https://blog.ninja-squad.com/2026/06/03/what-is-new-angular-22.0), [InfoQ](https://infoq.com/news/2026/08/angular-v22-released) | OPINIÓN | v22 cambia el valor por defecto a OnPush. Una migración agrega `Eager` a los componentes existentes que no declaran estrategia. |
| [Manfred Steyer, BASTA! 2025](https://speakerdeck.com/manfredsteyer/modern-angular-with-signals-and-signal-store-new-rules-for-your-architecture-at-bastacon-2025-in-frankfurt) y [AI-Ready Angular Architectures](https://speakerdeck.com/manfredsteyer/ai-ready-angular-architectures-architecture-as-executable-contract), 2026-09-30 | OPINIÓN | Sus diagramas siguen marcando "Smart Comp." y "Dumb Comp." dentro de cada feature, con capas Feature, UI, Data y Util. El store va del tamaño de una (sub)feature. |
| [Ouakala, DEV, 2026](https://dev.to/abdelaaziz_ouakala/smart-vs-presentational-components-in-angular-2026-where-does-ownership-really-belong-4hkm) | OPINIÓN | *"Signals didn't eliminate the Smart vs Presentational pattern."* *"The moment a 'presentational' component injects a service, it has broken its contract."* Pregunta guía: ¿el componente muestra datos o decide qué datos mostrar? |
| Dan Abramov, "Presentational and Container Components" (2015, nota de 2019) | OPINIÓN | Según resúmenes secundarios, en 2019 dejó de recomendar partir *todos* los componentes así. Con hooks, la lógica se separa sin un componente extra. No pude leer la nota original (403). |

### 1.1 Una contradicción de la skill oficial

La skill pone los subcomponentes en `features/<feature>/components/`. La guía de estilo de angular.dev pide evitar carpetas `components`. `conventions.md:31` también las prohíbe: "No hay carpetas por tipo técnico". Gana la guía de estilo, que coincide con el repo. [PRIMARIA + REPO]

---

## 2. Qué cambian los signals y OnPush

- **El estado local vive en el hijo.** Antes, un toggle o un borrador empujaba el estado al padre. Hoy un `signal()` o un `linkedSignal()` dentro del componente de presentación alcanza. Ejemplo: el título en edición de una fila. [INFERENCE]
- **`linkedSignal()` resuelve el borrador.** `draft = linkedSignal(() => this.task().title)` se reinicia solo cuando llega una tarea nueva por `input()`. No hace falta un `effect`. [PRIMARIA, `linked-signal.md`]
- **`model()` sirve para valores que el hijo cambia y el padre guarda.** Por ejemplo, la página actual: `[(page)]="page"`. Encaja mejor cuando la lectura de la lista dependa de ese signal (ver `resource()` abajo). [PRIMARIA]
- **`resource()` mueve la lectura al modelo reactivo.** La lista depende de `page()`. Con `resource({ params: () => ({ page: this.page() }), loader: … })`, cambiar la página recarga sola. Se van `load()`, `loading` y `error` (`tasks.ts:24-25`, `tasks.ts:50-63`). Las escrituras siguen con `TasksApi`. [PRIMARIA, `resource.md`]
- **OnPush recompensa la inmutabilidad.** Una fila con OnPush se revisa solo si su `task` cambia de referencia. El repo ya reemplaza objetos en lugar de mutarlos (`tasks.ts:191`). [PRIMARIA + REPO]
- **Los signals no reemplazan la regla de no inyectar.** Un hijo que inyecta `TasksApi` sigue escondiendo una dependencia: no se ve en sus entradas ni en sus salidas. [OPINIÓN]

---

## 3. Nombres y carpetas en este repo

| Idea | Cómo se llama aquí | Dónde vive |
|---|---|---|
| Componente de ruta | Nombre de la feature o de la pantalla: `Tasks`, `Profile`, `Login` | Una carpeta con su nombre: `features/tasks/tasks/`. Lo carga `loadComponent` en `tasks.routes.ts:6`. |
| Componente de presentación | Nombre de su propósito visual: `TaskRow`, `TaskPager`, `TaskDeleteDialog` | Una carpeta por componente en la feature: `features/tasks/task-row/task-row.ts` + `.html`. |
| Presentación compartida entre features | Por ejemplo `Button` | No existe hoy. `core/` no nombra features, pero es para lo transversal técnico. Una carpeta `ui/` sería una decisión nueva. |

- **Intent over Role se mantiene.** Nada de `TaskRowComponent` ni de `task-row.component.ts`. FRONT004 ya lo controla. [REPO]
- **FRONT011 ya admite este árbol.** Solo prohíbe archivos sueltos en la raíz de la feature. Una carpeta `task-row/` es una carpeta de propósito. [REPO, `check-frontend.mjs:254-259`]
- **El tipo de componente no va en la carpeta.** Lo define cómo se usa: si una ruta lo carga, es de ruta. El gate lo puede calcular (sección 6). [INFERENCE]
- **`ng generate component` hoy rompe el gate.** El schematic de 22.2.1 genera `task-pager.css` y `styleUrl` (lo comprobé con `--dry-run`), y FRONT009 rechaza `styleUrl`. `angular.json:11` tiene `"schematics": {}`. Lo trato en [informe-frontend-para-agentes.md](informe-frontend-para-agentes.md). [REPO]

---

## 4. Costos y beneficios

| Tema | A favor de partir | En contra |
|---|---|---|
| Prop drilling | Con un nivel (página → fila) no hay drilling: cada entrada pasa una vez. | Con tres niveles o más, cada dato baja por cada componente. Ahí conviene un servicio de la feature, no más `input()`. |
| Partir de más | Cada pieza tiene un propósito y un template corto. | Un componente de 10 líneas que solo reenvía entradas y salidas es una abstracción superficial. Agrega un archivo y no esconde nada. |
| Pruebas | Un componente de presentación se prueba sin `HttpClient` ni dobles: entradas, clics y salidas. | Hoy el repo no tiene suite de pruebas de frontend. El beneficio aparece solo si se adopta (⏸ DECISIÓN DE TOBI, ver el otro informe). |
| Reuso | `TaskPager` podría servir a otra lista. | Hoy no hay otra lista. El reuso no justifica el paso; la claridad sí. |
| Detección de cambios | Con OnPush, cambiar una fila no revisa las otras. | Con 10 filas por página la ganancia no se mide. No es el motivo. |
| Estilos | Un componente reúne clases de Tailwind repetidas: el botón secundario se repite 9 veces en `tasks.html`. | Cambia dónde viven las clases, no la regla de `conventions.md:43`. |

**Criterio que propongo** [INFERENCE]: partir cuando la pieza tiene estado propio de UI (borrador, diálogo abierto) o un contrato claro de entradas y salidas. No partir cuando la pieza necesita el resultado de una llamada a la API para seguir.

---

## 5. El frontend del repo frente a esto

| Componente | Tipo | Estado | Qué conviene |
|---|---|---|---|
| `App` (`app.ts`) | Raíz, la arranca `main.ts` | Inyecta `Session` y `Router`; maneja el logout. | Queda igual. Es una raíz, como un componente de ruta. |
| `Login`, `Register` | De ruta | Un formulario cada uno; inyectan `Session` y `Router`. | Quedan igual. |
| `Profile` | De ruta | Lee `/users/me` y guarda el perfil. | Queda igual. Más adelante, la lectura podría ser un `resource()`. |
| `Tasks` | De ruta | Lista, alta, edición, diálogo, paginación y manejo de foco: 12 signals (`tasks.ts:18-36`). | Partir en tres pasos (sección 6). |

Dos detalles de `Tasks` que el corte ayuda a resolver [REPO]:
- **La edición en línea no usa Signal Forms.** Usa `[value]` y `(input)` con `$any($event.target)` (`tasks.html:62-63`) y valida a mano (`tasks.ts:101-105`). `conventions.md:40` pide Signal Forms en los formularios. FRONT006 no lo detecta: solo mira el import de `@angular/forms`.
- **El componente toca el DOM directamente.** Abre el `<dialog>` (`tasks.ts:117-120`), guarda el botón que lo abrió (`tasks.ts:32`) y decide el foco después de borrar (`tasks.ts:166-184`).

---

## 6. Recomendación para `Tasks`

### 6.1 Forma final

```text
features/tasks/
├── index.ts, tasks.routes.ts
├── api/                      TasksApi y modelos (sin cambios)
├── tasks/                    Tasks: componente de ruta
├── task-pager/               TaskPager: presentación
├── task-row/                 TaskRow: presentación
└── task-delete-dialog/       TaskDeleteDialog: presentación
```

### 6.2 Qué va en cada pieza

**`Tasks` (de ruta)**. Inyecta `TasksApi`. Guarda la lista, la página, `pendingId`, `editingId` y la tarea a borrar. Llama a la API y decide el foco. Conserva el formulario de alta (`tasks.html:4-26`, `tasks.ts:36-44`).
- Por qué el alta queda aquí: `submission.action` espera la llamada a la API y usa el resultado para limpiar el formulario o mostrar `createError` (`tasks.ts:70-81`). Pasarlo a un hijo exige entradas `submitting` y `error`, una salida `created` y una forma de pedir el reinicio. Es más código para el mismo comportamiento. [INFERENCE]
- Por qué `editingId` queda aquí: hoy solo una fila edita a la vez (`tasks.ts:89-93`). Si cada fila guardara su propio "estoy editando", varias filas podrían editar a la vez. Eso cambia el comportamiento. [REPO + INFERENCE]

**`TaskPager` (presentación)**. Reemplaza `tasks.html:108-126`.
- Entradas: `page`, `totalPages`, `totalCount` (requeridas) y `disabled`.
- Salida: `pageChange: output<number>()`.
- Sin estado propio. Es el primer corte porque su contrato es el más simple.

**`TaskRow` (presentación)**. Reemplaza el `<li>` de `tasks.html:44-102`.
- Entradas: `task` (requerida), `editing` y `busy`. El padre calcula `[busy]="pendingId() === task.id"` y `[editing]="editingId() === task.id"`.
- Salidas: `completedChange: output<boolean>()`, `editStarted`, `editCancelled`, `renamed: output<string>()` y `removeRequested`.
- Estado local: el borrador con `linkedSignal(() => this.task().title)` y un `form()` de Signal Forms con `required` y `maxLength(200)`. Así se van `editTitle` (`tasks.ts:30`), la validación manual y el `$any`.

**`TaskDeleteDialog` (presentación)**. Reemplaza `tasks.html:129-158`.
- Entrada: `task: input<TaskResponse | null>()`. Con una tarea, el diálogo se abre; con `null`, se cierra.
- Salidas: `confirmed` y `cancelled`.
- Abre y cierra el `<dialog>` nativo con `afterRenderEffect`, que la skill admite para manipular el DOM (`effects.md`). Lo hace sobre su propio `viewChild`.
- El foco sigue en `Tasks`: el botón que abrió el diálogo está en una fila y el encabezado está en la página. Este es el corte con más riesgo: el paso debe probar el foco en el navegador.

### 6.3 Cómo fluye el estado

```text
TasksApi ──► Tasks (signals: items, page, pendingId, editingId, pendingDelete)
                │  input()                         ▲ output()
                ├─► TaskRow × N  ───── completedChange, renamed, removeRequested…
                ├─► TaskPager    ───── pageChange
                └─► TaskDeleteDialog ─ confirmed, cancelled
```

Los datos bajan por `input()`. Los hechos suben por `output()`. Solo `Tasks` llama a la API.

### 6.4 Lo que no propongo ahora

- **Un servicio de estado de la feature** (`TaskList` con `@Service({ autoProvided: false })`). Sirve cuando dos componentes de ruta comparten la lista. Hoy hay uno. [INFERENCE]
- **NgRx SignalStore.** Es una librería nueva: ⏸ DECISIÓN DE TOBI. No la recomiendo para este tamaño, igual que en [informe-sesion-angular.md](informe-sesion-angular.md).
- **La página en la URL** (`?page=2` con `withComponentInputBinding()`). Cambia lo que ve el usuario: es otro paso, si Tobi lo quiere.
- **Partir `Login`, `Register` o `Profile`.**

---

## 7. Una regla para el límite: FRONT013

### 7.1 Qué mira

1. **Qué componentes son de ruta.** En cada `*.routes.ts`, la regla toma el `import('…')` dentro de `loadComponent` y los identificadores de `component:`. Resuelve cada uno a su archivo, con el mismo `ts.resolveModuleName` que ya usa `collectEdges` (`check-frontend.mjs:271-305`). Suma la clase que recibe `bootstrapApplication` en `main.ts` (`App`).
2. **Qué inyecta cada componente que no es de ruta.** Para cada llamada `inject(...)` dentro de una clase con `@Component`, la regla pide al type checker el tipo de la expresión. Falla si ese tipo se declara bajo `frontend/src/app/` (un servicio de la app) o si es `Router` o `ActivatedRoute` de `@angular/router`. Es el mismo enfoque de FRONT012, que mira tipos y no texto (`check-frontend.mjs:194-201`).
3. **Constructores.** También falla un parámetro de constructor con uno de esos tipos. `conventions.md` pide `inject()`, pero así el constructor no sirve de atajo.

Lo que sí admite: `ElementRef`, `Injector`, `DestroyRef` y otros tokens de `@angular/*`.

**Mensaje propuesto**:

```text
FRONT013 frontend/src/app/features/tasks/task-row/task-row.ts:12 TaskRow is not a routed component and must not inject TasksApi; receive data with input() and report with output(), or load it from a component in tasks.routes.ts
```

### 7.2 Fixtures

El stub de `seedAngular` declara hoy `inject(t: unknown): unknown` (`check-frontend.test.mjs:45`). La regla necesita que `inject` devuelva el tipo del token: `inject<T>(token: abstract new (...args: never[]) => T): T`. Es compatible con los tests actuales. Hay que sumar un stub mínimo de `@angular/router` con `Router`.

| Caso | Archivos | Resultado esperado |
|---|---|---|
| Válida | `tasks.routes.ts` carga `./tasks/tasks`; `Tasks` inyecta `TasksApi`; `task-pager/task-pager.ts` inyecta solo `ElementRef` | Sin diagnósticos |
| Violación | `task-row/task-row.ts` hace `inject(TasksApi)` | Un FRONT013 en esa línea |
| Alias | `task-row.ts` hace `const api = TasksApi; inject(api)` | Un FRONT013: el tipo no cambia con el alias |
| Router | `task-row.ts` hace `inject(Router)` | Un FRONT013 |
| Raíz | `app.ts` inyecta `Session` y `main.ts` arranca `App` | Sin diagnósticos |

### 7.3 Límites de la regla

- No detecta un hijo que recibe un servicio por `input()`. Es raro y se ve en la revisión.
- No obliga a partir nada: un componente de ruta grande sigue pasando. La regla protege el límite; el tamaño lo decide quien diseña.
- Hoy pasa en verde sin cambios: todos los componentes del repo son de ruta o raíz. Su primera prueba real es el paso que crea `TaskPager`.

---

## 8. Pasos propuestos

Un concepto por paso. Dónde entran en el plan lo decide Tobi: el paso actual es el 2 de la Fase 1 (sección 9).

**A. Componente de ruta y componente de presentación: `TaskPager` + FRONT013.** Solo forma.
- **Concepto**: un componente de ruta orquesta; uno de presentación recibe `input()` y avisa con `output()`. El gate distingue uno de otro por las rutas.
- **Cambia**: `features/tasks/task-pager/` nuevo; `tasks.html:108-126` usa `<app-task-pager />`; FRONT013 en `check-frontend.mjs` con las fixtures de la sección 7.2; `conventions.md` y `architecture.md` con la regla.
- **Verifica**: `npm run build`; `node --test dune/check-frontend.test.mjs` con la violación deliberada en rojo; `check-frontend` en verde. Navegador: el recorrido de paginación de `verify-template/features/tasks.md` (11 tareas, `Siguiente` habilitado, "Página 2 de 2 (11)", `Anterior` vuelve).

**B. Estado local en el hijo: `TaskRow` con `linkedSignal` y Signal Forms.** Solo forma si el mensaje de error queda igual.
- **Concepto**: el borrador es estado de la fila; quién edita es estado de la página.
- **Cambia**: `features/tasks/task-row/`; `tasks.ts` pierde `editTitle`, `startEdit`, `cancelEdit` y la validación manual.
- **Verifica**: build y gates. Navegador: renombrar y ver `PUT` 200; título vacío → el mismo mensaje de hoy; `Cancelar`; abrir `Editar` en otra fila cierra la primera; el checkbox manda `PUT` con `isCompleted=true`; recargar y ver el dato.
- Si el mensaje pasa a mostrarse bajo el campo, como en los otros formularios, el paso **cambia comportamiento** y lo decide Tobi.

**C. Diálogo de presentación: `TaskDeleteDialog`.** Solo forma.
- **Concepto**: un hijo maneja su propio DOM con `viewChild` y `afterRenderEffect`; el padre decide el foco.
- **Cambia**: `features/tasks/task-delete-dialog/`; `tasks.ts` pierde `confirmDialog` y el `showModal()`.
- **Verifica**: build y gates. Navegador: `Eliminar` abre el diálogo con el título; `Escape` lo cierra y el foco vuelve al `Eliminar` de esa fila; `Confirmar` manda `DELETE` 204 y el foco va a "Mis tareas"; borrar la única tarea de la página 2 vuelve a la página 1.

**D. (Opcional) Lectura con `resource()`.** Puede cambiar comportamiento.
- **Concepto**: una lectura que depende de un signal.
- **Cambia**: `tasks.ts` reemplaza `load()`, `loading` y `error` por un `resource()` con `TasksApi.list` como loader; `TaskPager` puede usar `model()`.
- **Verifica**: navegador con la API cortada: "Sin conexión…" y `Reintentar`. Hay que mirar si la lista parpadea al recargar (estado `reloading`): si cambia lo que se ve, lo decide Tobi.

---

## 9. Decisiones de Tobi

- **Dónde entran estos pasos.** El paso actual es el 2 de la Fase 1. La regla 8 de AGENTS.md impide adelantar pasos. Opciones: pasos 0.17-0.19 antes del paso 2, o después.
- **Qué prohíbe FRONT013.** Propongo servicios de la app y `Router`/`ActivatedRoute`. Tobi puede preferir solo servicios de la app.
- **Dónde mostrar el error de la edición en línea** (paso B).
- **Una carpeta `ui/` para piezas compartidas entre features.** No hace falta todavía.
- ⏸ **NgRx SignalStore.** Librería nueva; no la recomiendo.
- ⏸ **Storybook** para ver los componentes de presentación aislados. Herramienta nueva; no la recomiendo para cuatro componentes.

---

## Verificación y límites

**Leído directamente**: [angular.dev/style-guide](https://angular.dev/style-guide); [best-practices.md](https://angular.dev/assets/context/best-practices.md) y [AGENTS.md de Angular](https://angular.dev/assets/context/AGENTS.md) (resumidos por la herramienta); [llms-full.txt](https://angular.dev/assets/context/llms-full.txt), descargado y buscado con `grep`; [Testing scenarios](https://angular.dev/guide/testing/components-scenarios); `components.md`, `inputs.md`, `outputs.md`, `queries-and-projection.md`, `naming-conventions.md`, `linked-signal.md`, `resource.md` y `testing-fundamentals.md` de la skill; `ChangeDetectionStrategy` en `@angular/core` 22.2.1; la plantilla y el `schema.json` del schematic `component` de `@schematics/angular`; `ng generate component … --dry-run`; los dos decks de Steyer y el artículo de DEV.

**Comprobado en el repo**: `check-frontend` OK (0,9 s); `node --test dune/check-frontend.test.mjs` 24 de 24 (4,8 s); `npm run build` OK (3 s); `ng g c --dry-run` genera `.css` y `.spec.ts`. El árbol de git no cambió.

**No verificado**:
- El anuncio oficial de v22 en blog.angular.dev: la búsqueda no lo devolvió. El cambio a OnPush sale de los tipos instalados y de fuentes secundarias.
- La nota de 2019 de Dan Abramov: Medium devolvió 403. Sale de resúmenes.
- El artículo de Thinktecture sobre componentes de presentación: devolvió una página de verificación.
- Los decks de Steyer son diapositivas sin texto: sé que dibuja "Smart" y "Dumb", no cómo los define hoy.
- Que `afterRenderEffect` abra el `<dialog>` sin problemas de foco: se prueba en el paso C.
- Que el `<dialog>` nativo devuelva solo el foco al botón que lo abrió: no lo probé. Por eso el foco queda en `Tasks`.

## Fuentes

Consultadas el 2026-10-17.

- [Angular style guide](https://angular.dev/style-guide), angular.dev. [PRIMARIA]
- [best-practices.md](https://angular.dev/assets/context/best-practices.md) y [AGENTS.md](https://angular.dev/assets/context/AGENTS.md), angular.dev. [PRIMARIA]
- [llms-full.txt](https://angular.dev/assets/context/llms-full.txt), angular.dev. [PRIMARIA]
- [Component testing scenarios](https://angular.dev/guide/testing/components-scenarios), angular.dev. [PRIMARIA]
- [angular/skills, `angular-developer`](https://github.com/angular/skills/tree/main/angular-developer), copia local en `.claude/skills/angular-developer/`. [PRIMARIA]
- `@angular/core` 22.2.1 y `@schematics/angular` 22.2.1, `frontend/node_modules`. [PRIMARIA]
- [What's new in Angular 22.0](https://blog.ninja-squad.com/2026/06/03/what-is-new-angular-22.0), Ninja Squad, 2026-06-03. [OPINIÓN]
- [Angular v22 released](https://infoq.com/news/2026/08/angular-v22-released), InfoQ, 2026-08. [OPINIÓN]
- [Modern Angular with Signals and Signal Store](https://speakerdeck.com/manfredsteyer/modern-angular-with-signals-and-signal-store-new-rules-for-your-architecture-at-bastacon-2025-in-frankfurt), Manfred Steyer, 2025-03-03. [OPINIÓN]
- [AI-Ready Angular Architectures](https://speakerdeck.com/manfredsteyer/ai-ready-angular-architectures-architecture-as-executable-contract), Manfred Steyer, 2026-09-30. [OPINIÓN]
- [Smart vs Presentational Components in Angular 2026](https://dev.to/abdelaaziz_ouakala/smart-vs-presentational-components-in-angular-2026-where-does-ownership-really-belong-4hkm), A. Ouakala, DEV, mayo 2026. [OPINIÓN]
- [Smart and Dumb Components](https://medium.com/@dan_abramov/smart-and-dumb-components-7ca2f9a7c7d0), Dan Abramov, 2015 (nota de 2019). No leída: 403. [OPINIÓN]
