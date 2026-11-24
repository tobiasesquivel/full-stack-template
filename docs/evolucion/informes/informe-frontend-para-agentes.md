# Informe: un frontend que los agentes de código manejan bien

**Alcance**: solo investigación. Leí la documentación de Anthropic (Claude Code), OpenAI (Codex), agents.md, angular.dev (sección de IA), Playwright y dos fuentes reconocidas de ingeniería. Comparé eso con el repo: AGENTS.md, los gates de Dune, `check-frontend`, las skills y `verify-template`. Corrí los gates del frontend y algunos comandos del CLI sin cambiar archivos. No cambié código.

**Distinción de fuentes**: **[PRIMARIA]** = el proveedor de la herramienta o el código instalado. **[OPINIÓN]** = practicante que interpreta. **[INFERENCE]** = razonamiento mío. **[REPO]** = observado en el código, con `archivo:línea`.

**Términos**:
- **Agente**: una herramienta que lee, edita y ejecuta comandos sola: Claude Code, Codex, Cursor o el agente de Copilot.
- **Chequeo**: un comando que devuelve pasa o falla sin que una persona mire. Un build, un test, un gate.
- **Gate**: un chequeo que hace cumplir una regla del repo, como `check-frontend`.

---

## Conclusión

1. **Lo que más ayuda a un agente es un chequeo que pueda correr solo.** Anthropic lo pone primero en sus buenas prácticas: *"Give Claude a check it can run: tests, a build, a screenshot to compare."* Sin chequeo, la persona hace de chequeo. [PRIMARIA]
2. **El repo ya está por encima del promedio.** Tiene AGENTS.md corto con reglas, docs como fuente de verdad, gates con código de regla y `archivo:línea`, una sola forma para cada cosa (Signal Forms, `@Service()`, Tailwind), skills fijadas por hash y una skill de verificación con evidencia. TypeScript 6 y Angular 22 ya traen `strict` y `strictTemplates` activos por defecto. [REPO + PRIMARIA]
3. **El hueco mayor es el chequeo de comportamiento del frontend.** No hay ningún `*.spec.ts`. La única prueba de la UI es un recorrido en Markdown que un agente conduce a mano con un navegador. Es lento, necesita backend y PostgreSQL, y no se repite igual dos veces. [REPO]
4. **Vitest ya está instalado y conectado; solo faltan las pruebas.** `angular.json:71` usa `@angular/build:unit-test`, cuyo runner por defecto es Vitest, y `package.json` trae `vitest` y `jsdom`. `ng test` hoy falla con *"No tests found"*. `frontend/README.md:8` dice que "npm test no tiene runner configurado": eso ya no es cierto. [REPO]
5. **`ng generate component` produce código que el gate rechaza.** Genera un `.css` y `styleUrl`, y FRONT009 prohíbe `styleUrl`. La skill de Angular pide usar el CLI para generar código. Un ajuste en `angular.json` lo resuelve sin herramientas nuevas. [REPO]
6. **El servidor MCP del CLI de Angular no está configurado.** Ofrece búsqueda en la documentación, la guía de buenas prácticas de la versión instalada, `run_target` y el dev server. En 22.2.1 todas sus herramientas son estables. Configurarlo es ⏸ DECISIÓN DE TOBI. [PRIMARIA]
7. **El paso 2 del plan ya apunta en esta dirección.** "Un solo comando de verificación local" es justo lo que un agente necesita. Las pruebas de frontend deberían entrar en ese comando cuando existan. [REPO]

---

## 1. Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [Claude Code, Best practices](https://code.claude.com/docs/en/best-practices) | PRIMARIA | *"Give Claude a way to verify its work."* El chequeo es *"a test suite, a build exit code, a linter, a script that diffs output against a fixture, or a browser screenshot"*. Pedir evidencia, no afirmaciones. Un *Stop hook* convierte el chequeo en un gate determinista. CLAUDE.md corto: *"Would removing this cause Claude to make mistakes?"* Lo que importa solo a veces va en skills. |
| [Claude Code, Memory](https://code.claude.com/docs/en/memory) | PRIMARIA | Claude Code lee `AGENTS.md` directamente desde v2.1.277, o por import desde CLAUDE.md. Meta: menos de 200 líneas por archivo. *"Imports … don't reduce its context cost."* Las reglas por ruta (`.claude/rules/`) cargan solo con los archivos que tocan. |
| [Codex, AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md) | PRIMARIA | Codex concatena los `AGENTS.md` desde la raíz hasta la carpeta actual; el más cercano gana. Tope de 32 KiB por defecto. Ejemplo de regla: *"Run `npm run lint` before opening a pull request."* |
| [agents.md](https://agents.md/) | PRIMARIA | *"A simple, open format for guiding coding agents."* Contenido sugerido: comandos de build y test, estilo, instrucciones de prueba y de PR. El archivo más cercano tiene precedencia. Lo administra la Agentic AI Foundation, de la Linux Foundation. |
| [Develop with AI](https://angular.dev/ai/develop-with-ai) | PRIMARIA | Archivos de reglas por herramienta, `best-practices.md`, [llms.txt](https://angular.dev/llms.txt) y `llms-full.txt`. Los archivos *"will be updated on a regular basis."* No menciona CLAUDE.md. |
| [Angular CLI MCP](https://angular.dev/ai/mcp) | PRIMARIA | `npx -y @angular/cli mcp`. Herramientas: `get_best_practices`, `search_documentation`, `list_projects`, `run_target`, `devserver_start`, `devserver_stop`, `devserver_wait_for_build`, `onpush_zoneless_migration` y `ai_tutor`. Opciones `--read-only` y `--local-only`. |
| [Agent skills](https://angular.dev/ai/agent-skills) | PRIMARIA | Skills oficiales `angular-developer` y `angular-new-app`, desde `github.com/angular/skills`. |
| `angular-developer` (`SKILL.md`, `testing-fundamentals.md`, `e2e-testing.md`) | PRIMARIA | *"Once you finish generating code, run `ng build`."* Pruebas con Vitest y `TestBed`, patrón "Act, Wait, Assert" con `await fixture.whenStable()`. E2E con `ng add playwright-ng-schematics` si no hay uno configurado. |
| [Angular testing](https://angular.dev/assets/context/llms-full.txt) (sección de Vitest) | PRIMARIA | *"the default testing setup for new Angular CLI projects, which uses Vitest."* Corre en Node con `jsdom`; la opción `browsers` usa el modo navegador de Vitest y pide instalar un proveedor. |
| [Playwright, Best practices](https://playwright.dev/docs/best-practices) | PRIMARIA | Probar lo que ve el usuario. Preferir `getByRole` a CSS o XPath: *"Your DOM can easily change."* Cada test aislado. Aserciones que esperan solas. |
| [Böckeler, "Harness Engineering – first thoughts"](https://www.martinfowler.com/articles/exploring-gen-ai/harness-engineering-memo.html), 2026-02-17 | OPINIÓN | El equipo de OpenAI controla la arquitectura con *"deterministic custom linters and structural tests"* y con agentes que buscan desvíos. *"increasing trust and reliability required constraining the solution space."* Le falta la *"verification of functionality and behaviour."* |
| OpenAI, "Harness engineering", 2026-02-11 | OPINIÓN (resumida por terceros) | AGENTS.md como índice que apunta a `docs/`, no como enciclopedia. La app se vuelve legible para el agente: DevTools en el navegador y observabilidad por worktree. No pude leer el post (403). |
| [Steyer, "AI-Ready Angular Architectures"](https://speakerdeck.com/manfredsteyer/ai-ready-angular-architectures-architecture-as-executable-contract), 2026-09-30 | OPINIÓN | "Architecture as Executable Contract": reglas en `docs/`, cargadas desde AGENTS.md cuando hacen falta; límites con Sheriff; tests de arquitectura; un *Stop hook* en `.claude/settings.json`; el MCP del CLI de Angular; skills oficiales. |

---

## 2. Qué hace que un código sea fácil para un agente

Ordenado por impacto, según las fuentes de la sección 1. [INFERENCE sobre PRIMARIA y OPINIÓN]

1. **Chequeos rápidos y deterministas que el agente corre solo.** Build, tests unitarios y de componentes, gates de arquitectura. Cada uno devuelve un código de salida. Cuanto más rápido, más veces lo corre.
2. **Mensajes de error que dicen qué hacer.** "Usá X en lugar de Y", con `archivo:línea`. El agente corrige sin adivinar.
3. **Una sola forma de hacer cada cosa, y un gate que la sostiene.** Menos opciones, menos errores. Es "restringir el espacio de soluciones" de Böckeler.
4. **Instrucciones cortas que apuntan a docs.** AGENTS.md como índice; el detalle en `docs/` y en skills que cargan solo cuando hacen falta.
5. **Límites explícitos.** Una puerta por módulo (`index.ts`), archivos chicos, un tipo por archivo. El agente lee menos para entender más.
6. **Tipos estrictos.** El compilador encuentra errores antes que el test.
7. **Una prueba de navegador repetible.** Selectores por rol y nombre accesible, no por clases. Un script que corre igual cada vez, no una receta que se interpreta.
8. **Herramientas que generan código válido.** Si el generador oficial produce algo que el gate rechaza, el agente pierde una vuelta en cada componente.

---

## 3. Lo que el repo ya tiene

| Práctica | Evidencia | Estado |
|---|---|---|
| AGENTS.md corto, con reglas y un índice de docs | `AGENTS.md` (~80 líneas); `CLAUDE.md` es `@AGENTS.md` | Cumple. Coincide con Claude Code y Codex. |
| Docs como fuente de verdad | ADR-0004, `architecture.md`, `conventions.md`, `docs/evolucion/` | Cumple |
| Gates con código de regla y mensaje que dice qué hacer | `check-frontend.mjs:431-482`; ejemplo: `use provideAppInitializer() instead of the APP_INITIALIZER token` | Cumple. Corre en 0,9 s. |
| Cada regla con fixture válida y violación deliberada | `check-frontend.test.mjs`: 24 tests en 4,8 s | Cumple |
| Una forma para cada cosa | FRONT005 (`@Service()`), FRONT006 (Signal Forms), FRONT008 y FRONT009 (template aparte, Tailwind), FRONT012 (estado privado) | Cumple |
| Puertas explícitas y archivos chicos | FRONT001 (`index.ts`), FRONT010 (un tipo por archivo), FRONT011 (carpetas de propósito) | Cumple |
| Tipos estrictos | Sin `strict` en `tsconfig.json`, pero TypeScript 6.0.3 lo activa por defecto (`getStrictOptionValue({}, …)` = `true`). Angular 22: *"strictTemplate is `true` by default"* en `compiler-cli`. | Cumple, de forma implícita |
| Build rápido | `npm run build` en 3 s | Cumple |
| Skills fijadas | `skills-lock.json` con hash de `angular-developer` y `angular-new-app` | Cumple |
| Verificación con evidencia y estados `PASS`/`FAIL`/`NOT_RUN` | `.agents/skills/verify-template/` | Cumple |
| Selectores accesibles en las recetas | `features/tasks.md`: `#new-task-title`, "Marcar hecha: …", el nav `Paginación` | Cumple. Ya están listos para `getByRole`. |

---

## 4. Lo que falta, por prioridad

**P1. Pruebas unitarias y de componentes en el frontend.** ⏸ DECISIÓN DE TOBI (adoptar la suite).
- Hoy: cero `*.spec.ts`. `ng test --watch=false` falla con *"No tests found matching … `**/*.spec.ts`"*. CI no corre `ng test` (`ci.yml:67-72`). [REPO]
- La herramienta ya está: Vitest es el runner por defecto del builder, y `vitest` y `jsdom` están en `devDependencies`. No hace falta instalar nada para el modo `jsdom`. [PRIMARIA + REPO]
- Qué probar primero: funciones puras (`returnTarget`, `describeHttpError`), la máquina de estados de `Session` con `HttpTestingController`, y componentes de presentación como `TaskPager` con `inputBinding`/`outputBinding` (ver [informe-componentes-angular.md](informe-componentes-angular.md)).
- El modo navegador de Vitest (`browsers: ["chromium"]`) pide `@vitest/browser` y Playwright: herramienta nueva, ⏸ DECISIÓN DE TOBI. Para empezar, `jsdom` alcanza.
- Arreglar a la vez `frontend/README.md:8`, `README.md:92` y `AGENTS.md:68`, que dicen que no hay suite. **Solo forma** (docs).

**P2. Un solo comando de verificación (paso 2 del plan).** Ya planificado.
- Hoy el README lista 8 comandos de raíz y 3 de frontend, con un orden que importa (`check-comments` después de `npm ci`). [REPO]
- Conviene que el comando tenga un modo rápido solo de frontend: build, gate, tests del gate y, cuando existan, `ng test`. Un agente lo corre en cada cambio sin Docker. [INFERENCE]

**P3. El CLI genera código que el gate rechaza.** Solo forma.
- `ng g c features/tasks/task-pager --dry-run` crea `task-pager.css` y el `.ts` con `styleUrl`. FRONT009 lo rechaza. [REPO]
- Arreglo: `"schematics": { "@schematics/angular:component": { "style": "none" } }` en `angular.json:11`. Si se adopta P1, se mantiene el `.spec.ts`; si no, `"skipTests": true`.
- No es una herramienta nueva: es la configuración del CLI que el repo ya usa.

**P4. Prueba de navegador repetible (e2e con Playwright).** ⏸ DECISIÓN DE TOBI (herramienta nueva).
- Hoy: las recetas de `verify-template/features/*.md` son buenas, pero un agente las interpreta cada vez. El resultado depende de cómo las lea. [REPO]
- Propuesta: llevar el recorrido mínimo (registrar, crear tarea, recargar, ver la tarea) a un spec con `getByRole`. Lo corre `verify.mjs` contra su propia instancia. Las recetas siguen para lo exploratorio.
- Alternativa sin herramienta nueva: ninguna buena. El harness del agente trae su navegador, pero el repo no.

**P5. Formato verificado.** ⏸ DECISIÓN DE TOBI.
- Hoy: `prettier` y `.prettierrc` están en `frontend/`, pero no hay script ni gate. [REPO]
- `prettier --check` en el comando del paso 2 evita diffs con ruido de formato. La herramienta ya está instalada; activar el gate cambia una regla.
- ESLint con `angular-eslint` sería una herramienta nueva. Gran parte de lo que daría ya lo cubre `check-frontend`. No lo recomiendo ahora.

**P6. Servidor MCP del CLI de Angular.** ⏸ DECISIÓN DE TOBI.
- Hoy: no hay `.mcp.json`. [REPO]
- Qué aporta: `search_documentation` y `get_best_practices` de la versión instalada; `run_target` y el dev server como herramientas. La skill ya cubre gran parte de la guía. [PRIMARIA]
- Si se adopta: `.mcp.json` en la raíz con `"args": ["-y", "@angular/cli", "mcp", "--read-only"]`. Así el MCP no modifica el proyecto. En 22.2.1 no hay herramientas experimentales (`EXPERIMENTAL_TOOLS = []` en `mcp-server.js`).

**P7. Un gate al final de cada turno del agente (Stop hook).** ⏸ DECISIÓN DE TOBI (configuración del agente).
- Claude Code puede correr un script al terminar cada turno y bloquear el cierre si falla. Steyer lo usa con `.claude/settings.json`. [PRIMARIA + OPINIÓN]
- Encaja con el comando rápido de P2. Solo aplica a Claude Code; Codex y Cursor necesitan su propio mecanismo.

**P8. Límite entre componente de ruta y de presentación (FRONT013).** Ver [informe-componentes-angular.md](informe-componentes-angular.md), sección 7. Solo forma.

**P9. AGENTS.md del frontend.** Solo forma. Prioridad baja.
- Un `frontend/AGENTS.md` con los comandos del frontend se cargaría al trabajar ahí: Codex lo concatena solo. Para Claude Code haría falta además un `frontend/CLAUDE.md` con `@AGENTS.md`. Hoy AGENTS.md es corto: no hay apuro. [PRIMARIA + INFERENCE]

**Lo que no recomiendo**:
- **`data-testid` en los templates.** Los roles y nombres accesibles ya identifican cada control, y Playwright los prefiere. Un `data-testid` solo cuando no haya rol ni nombre.
- **Storybook.** Herramienta nueva (⏸) con poco valor para cuatro componentes de presentación.

---

## 5. Pasos propuestos

Un cambio observable por paso. Dónde entran en el plan lo decide Tobi.

**1. El CLI genera código que pasa el gate.** Solo forma.
- **Cambia**: `angular.json` (`style: "none"` para componentes).
- **Verifica**: `ng g c features/tasks/x --dry-run` no lista `.css`. Gate: generar un componente en una fixture temporal o en una rama descartable y correr `check-frontend` en verde (antes, FRONT009 en rojo). Navegador: recorrido mínimo de `verify-template` sin cambios, como regresión.

**2. Primera suite de frontend con Vitest.** ⏸ DECISIÓN DE TOBI. Solo forma (no cambia la app).
- **Cambia**: `return-url.spec.ts` y `http-error.spec.ts`; `ng test --watch=false` en el README y en el job `frontend` de CI; los tres docs que dicen que no hay suite.
- **Verifica**: `ng test --watch=false` en verde; una violación deliberada (por ejemplo, que `returnTarget('//evil.com')` devuelva esa URL) lo pone en rojo. Navegador: recorrido mínimo sin cambios.

**3. Pruebas de componente con `TestBed`.** Depende del paso 2 y del paso A del otro informe.
- **Cambia**: `task-pager.spec.ts` con `inputBinding`/`outputBinding` y "Act, Wait, Assert".
- **Verifica**: el spec pasa; cambiar la condición de `disabled` del botón `Siguiente` lo pone en rojo. Navegador: la paginación de `features/tasks.md`.

**4. Un comando de verificación con modo frontend.** Es el paso 2 del plan, con el alcance de arriba.
- **Verifica**: lo que pide el plan, más el modo frontend con el mismo resultado que los comandos sueltos.

**5. E2E con Playwright.** ⏸ DECISIÓN DE TOBI.
- **Cambia**: un spec con el recorrido mínimo, con `getByRole`; `verify.mjs` lo corre contra su instancia.
- **Verifica**: el spec pasa contra la API real; con el botón `Crear` renombrado falla con un mensaje que nombra el rol buscado.

**6. MCP del CLI de Angular, solo lectura.** ⏸ DECISIÓN DE TOBI.
- **Verifica**: el agente lista las herramientas y `list_projects` devuelve `frontend`.

---

## 6. Decisiones de Tobi

- ⏸ **Adoptar la suite de Vitest** (ya instalada). Suma un gate al README y a CI.
- ⏸ **Modo navegador de Vitest** (`@vitest/browser` + Playwright). Herramienta nueva. Propongo `jsdom` primero.
- ⏸ **Playwright para e2e.** Herramienta nueva.
- ⏸ **`prettier --check` como gate.** La herramienta ya está; cambia una regla.
- ⏸ **ESLint con `angular-eslint`.** Herramienta nueva. No lo recomiendo ahora.
- ⏸ **MCP del CLI de Angular** y en qué modo (`--read-only` propuesto).
- ⏸ **Stop hook** en `.claude/settings.json`.
- ⏸ **Storybook.** No lo recomiendo.
- **Orden en el plan**: estos pasos frente al paso 2 de la Fase 1, que es el actual.

---

## Verificación y límites

**Comprobado en el repo** (2026-10-17, rama `fase0/16-session-tabs`): `node dune/check-frontend.mjs --root .` OK en 0,9 s; `node --test dune/check-frontend.test.mjs` 24 de 24 en 4,8 s; `npm run build` OK en 3 s; `npx ng test --watch=false` falla con "No tests found"; el `schema.json` del builder `unit-test` declara `runner` con valor por defecto `vitest`; `npx ng generate component … --dry-run` lista `.css` y `.spec.ts`; la plantilla del schematic escribe `styleUrl` salvo con `style: none`; `ts.getStrictOptionValue({}, 'strictNullChecks')` = `true` en TypeScript 6.0.3; `compiler-cli` 22.2.1 trata `strictTemplates` como `true` salvo `false` explícito; `mcp-server.js` del CLI 22.2.1 declara `EXPERIMENTAL_TOOLS = []`. El árbol de git no cambió.

**No verificado**:
- El post de OpenAI "Harness engineering" devolvió 403. Uso solo lo que confirman Böckeler y resúmenes de terceros.
- Que Claude Code cargue un `AGENTS.md` de una subcarpeta sin un `CLAUDE.md` al lado. La doc lo describe para la raíz; no lo probé en subcarpetas.
- La diapositiva de Steyer dice "Claude now supports AGENTS.MD (Sep 2026)". La doc de Claude Code pide v2.1.277 o posterior, sin fecha.
- No medí cuánto tarda una suite de Vitest en este repo: no existe todavía.
- Las páginas se leyeron con una herramienta que resume. Las citas son textuales según esa herramienta.

## Fuentes

Consultadas el 2026-10-17.

- [Best practices for Claude Code](https://code.claude.com/docs/en/best-practices), Anthropic. [PRIMARIA]
- [How Claude remembers your project](https://code.claude.com/docs/en/memory), Anthropic. [PRIMARIA]
- [AGENTS.md en Codex](https://learn.chatgpt.com/docs/agent-configuration/agents-md), OpenAI. [PRIMARIA]
- [agents.md](https://agents.md/), Agentic AI Foundation. [PRIMARIA]
- [Develop with AI](https://angular.dev/ai/develop-with-ai), [Angular CLI MCP server](https://angular.dev/ai/mcp) y [Agent skills](https://angular.dev/ai/agent-skills), angular.dev. [PRIMARIA]
- [llms.txt](https://angular.dev/llms.txt) y [llms-full.txt](https://angular.dev/assets/context/llms-full.txt), angular.dev. [PRIMARIA]
- [angular/skills, `angular-developer`](https://github.com/angular/skills/tree/main/angular-developer), copia local. [PRIMARIA]
- `@angular/build`, `@angular/cli`, `@angular/compiler-cli` y `@schematics/angular` 22.2.1; `typescript` 6.0.3, en `frontend/node_modules`. [PRIMARIA]
- [Best Practices](https://playwright.dev/docs/best-practices), Playwright. [PRIMARIA]
- [Harness Engineering – first thoughts](https://www.martinfowler.com/articles/exploring-gen-ai/harness-engineering-memo.html), Birgitta Böckeler, martinfowler.com, 2026-02-17. [OPINIÓN]
- [Harness engineering](https://openai.com/index/harness-engineering/), Ryan Lopopolo, OpenAI, 2026-02-11. No leído: 403. Resumen en [rywalker.com](https://rywalker.com/research/openai-harness). [OPINIÓN]
- [AI-Ready Angular Architectures: Architecture as Executable Contract](https://speakerdeck.com/manfredsteyer/ai-ready-angular-architectures-architecture-as-executable-contract), Manfred Steyer, 2026-09-30. [OPINIÓN]
