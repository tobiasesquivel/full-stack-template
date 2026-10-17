# Informe: Angular moderno — nombres, servicios y buenas prácticas (v20-v22)

**Alcance**: solo investigación. Leí fuentes web primarias, el código fuente del CLI de Angular, la skill oficial `angular/skills` y el frontend del repo. No cambié código. Al final está la lista de lo verificado y lo no verificado.

**Distinción de fuentes**: **[PRIMARIA]** = el equipo de Angular (angular.dev, el blog oficial, los repos `angular/angular`, `angular/angular-cli` y `angular/skills`). **[OPINIÓN]** = practicante que interpreta a otros. **[INFERENCE]** = razonamiento mío sobre lo leído.

---

## Conclusión

1. **Desde v20, Angular no usa sufijos de tipo en archivos ni en clases.** `user-profile.component.ts` / `UserProfileComponent` pasa a `user-profile.ts` / `UserProfile`. El CLI ya no los genera para componentes, directivas, servicios ni pipes. Los proyectos que se actualizan con `ng update` los conservan, porque una migración agrega opciones a `angular.json`. [PRIMARIA]
2. **El nombre dice el propósito, no el tipo técnico.** La skill oficial lo llama *Intent over Role*: un servicio se llama por lo que hace (`auth-data.ts` / `AuthData`, `user-data-client.ts` / `UserDataClient`). La carpeta da el contexto. [PRIMARIA]
3. **Angular v22 agrega `@Service()`.** Reemplaza a `@Injectable({ providedIn: 'root' })` en los servicios nuevos: una sola instancia para toda la app, sin `providers`, y fuera del bundle si nadie lo inyecta. `ng generate service auth` crea hoy `auth.ts` con `@Service() export class Auth`. [PRIMARIA]
4. **La skill más usada es la oficial**: `angular-developer`, de `angular/skills`, con unos 35 mil installs en skills.sh. Le siguen `angular-new-app` (oficial, unos 14 mil) y `angular-component` de AnalogJS (comunidad, unos 11 mil). [PRIMARIA para el origen; las cifras cambian cada semana]
5. **Este repo está cerca de la forma moderna.** No usa `*ngIf`, `ngClass`, `@Input`, `@HostBinding`, `standalone: true` ni `OnPush` explícito, y usa `inject()` y signals. Le faltan tres cosas: nombres sin sufijo en los servicios (hoy mezcla los dos estilos), `@Service()` y Signal Forms. También usa `APP_INITIALIZER` donde existe la forma funcional `provideAppInitializer()`. [Observado en el repo]

---

## 1. Nombres de archivos y clases

### 1.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [Guía de estilo](https://angular.dev/style-guide) | PRIMARIA | *"Separate words within a file name with hyphens"*. *"File names should generally describe the contents of the code in the file."* Pide organizar por feature: *"Avoid creating subdirectories based on the type of code"*. Los tests terminan en `.spec.ts`. No menciona sufijos de tipo. |
| [Anuncio de v20](https://blog.angular.dev/announcing-angular-v20-b5c9c06cf301) | PRIMARIA | Los sufijos de archivo y de clase pasan a ser opcionales, para nombrar las abstracciones con intención. Desde v20, el CLI no genera sufijos para componentes, directivas, servicios ni pipes. |
| [angular-cli#30050](https://github.com/angular/angular-cli/pull/30050) | PRIMARIA | `ng update` a v20 agrega a `angular.json` `type: 'component'`, `type: 'service'`, `typeSeparator: '.'`, etc., para que los proyectos existentes sigan generando como antes. |
| Plantilla del schematic `service` (`packages/schematics/angular/service/files/`) | PRIMARIA | Genera `@Service()` salvo que se pase `--injectable`. La opción `type` agrega un sufijo al archivo, y `addTypeToClassName` también al nombre de la clase. |
| [`naming-conventions.md`](https://github.com/angular/skills/blob/main/angular-developer/references/naming-conventions.md) de la skill oficial | PRIMARIA | *Intent over Role*. Archivo y clase coinciden (`product-list.ts` → `class ProductList`). Los servicios usan nombres de propósito: `-data`, `-store`, `-api`, `-client`. Respeta la convención del proyecto y no mezcla estilos en una misma carpeta de feature. Los modelos conservan `.model.ts`. |
| Posts de Medium y blogs | OPINIÓN | Algunos dicen que "la palabra service desaparece". Va más allá de la fuente oficial. |

### 1.2 Una contradicción de la skill oficial

La regla general dice que las clases pierden el sufijo `Service`. El ejemplo de `core/` dice *"New: `auth.ts` (Class: `AuthService`)"*. El CLI resuelve la duda: `ng generate service auth` genera `class Auth`. [PRIMARIA, verificado en la plantilla]

### 1.3 Guards, interceptores y modelos

- **Guards e interceptores.** La migración de v20 conserva `typeSeparator: '.'` en los proyectos viejos. Eso indica que el estilo nuevo usa guion: `auth-guard.ts`, `auth-interceptor.ts`. [INFERENCE a partir de la migración]
- **Modelos.** La skill pide `.model.ts` para no chocar con un componente del mismo nombre. La guía oficial no lo pide. Un archivo `tasks-models.ts`, con varios tipos y sin punto, cumple la regla del guion de la guía. [INFERENCE]

---

## 2. Servicios en v22: `@Service()`

| Fuente | Tipo | Qué dice |
|---|---|---|
| [`creating-services.md`](https://github.com/angular/skills/blob/main/angular-developer/references/creating-services.md) | PRIMARIA | `@Service()` es *"the recommended approach for most services"*: crea una sola instancia, la hace disponible en toda la app sin `providers` y permite tree-shaking. `@Service({ autoProvided: false })` sirve para una instancia por componente. |
| [best-practices.md](https://angular.dev/assets/context/best-practices.md) | PRIMARIA | *"Prefer the `@Service` decorator over `@Injectable({providedIn: 'root'})` for new singleton services (Angular v22+)"*. |
| `@angular/core` 22.2.1 instalado en el repo (`types/core.d.ts`) | PRIMARIA | Declara `ServiceDecorator` y la opción `autoProvided`. |

---

## 3. Otras prácticas vigentes

Fuentes: [best-practices.md](https://angular.dev/assets/context/best-practices.md), [Develop with AI](https://angular.dev/ai/develop-with-ai) y la [guía de estilo](https://angular.dev/style-guide). [PRIMARIA]

- **Valores por defecto:** `standalone` desde v20 y `OnPush` desde v22. No se escriben.
- **Componentes:** `input()`, `output()` y `model()`. Bindings `class` y `style`, no `ngClass` ni `ngStyle`. Los bindings del host van en el objeto `host`, no con `@HostBinding` ni `@HostListener`. Los miembros que usa el template van `protected`. Lo que inicializa Angular va `readonly`.
- **Estado:** `signal()` para el estado local, `computed()` para lo derivado y `linkedSignal()` para lo que depende de varias fuentes. `set()` o `update()`, nunca `mutate()`. Para leer datos async, `resource()` y `httpResource()`.
- **Formularios:** desde v22, Signal Forms (`@angular/forms/signals`) para los formularios nuevos. En los que ya existen, la estrategia que use la app.
- **Templates:** `@if`, `@for` y `@switch`. Las handlers se nombran por la acción (`saveUserData()`), no por el evento (`handleClick()`).
- **DI:** `inject()`, no el constructor.
- **Tests:** Vitest con `TestBed`.

---

## 4. Skills de Angular

| Skill | Publicador | Installs | Qué hace |
|---|---|---|---|
| `angular-developer` | `angular/skills` (Google) | ~35,5 mil ([skills.sh](https://www.skills.sh/angular)); ~42,8 mil según [openskills.app](https://openskills.app/skills/angular-developer) | Índice de ~40 referencias: componentes, signals, formularios, DI, router, SSR, accesibilidad, testing, nombres, CLI. |
| `angular-new-app` | `angular/skills` (Google) | ~14,5 mil | Crea una app nueva con el CLI. |
| `angular-component` | `analogjs/angular-skills` (comunidad) | ~10,8 mil | Componentes. |
| Skills dentro de `angular/angular` | Google | ~9,2 mil en total | `angular-developer`, `reference-core`, `reference-signal-forms` y otras. |

`angular/skills` es un espejo publicado: las contribuciones van a `angular/angular` ([InfoQ](https://www.infoq.com/news/2026/06/angular-agent-skills/)). La skill pide revisar la versión de Angular del proyecto antes de aconsejar, y correr `ng build` después de generar código.

---

## 5. El frontend del repo frente a esto

Observado en `frontend/src/app` (rama del paso 0.6):

| Práctica | Estado |
|---|---|
| Control flow nativo, `inject()`, signals, sin `standalone`/`OnPush` explícitos, sin `ngClass`/`@HostBinding`/`@Input` | Cumple |
| Nombres sin sufijo | Mezcla. Los componentes ya no tienen sufijo (`login.ts` → `Login`). Los servicios sí (`auth.service.ts` → `AuthService`), igual que los guards (`auth.guard.ts`) y el interceptor (`auth.interceptor.ts`). |
| `@Service()` | No. `AuthService`, `UsersService` y `TasksService` usan `@Injectable({ providedIn: 'root' })`. |
| `angular.json` | `"schematics": {}`. Un `ng generate service` crearía hoy el estilo nuevo, distinto de los servicios existentes. |
| Formularios | Reactive Forms (`FormBuilder`) en login, registro, tareas y perfil. |
| Inicialización | `APP_INITIALIZER` con `useFactory`. En la 22.2.1 no está marcado `@deprecated`; existe `provideAppInitializer()`. |

---

## 6. Pasos propuestos

Tobi aprobó la modernización (2026-10-17). Un concepto por paso:

1. **Servicios y nombres de v22**: nombres de propósito sin sufijo, `@Service()` y un gate que lo haga cumplir.
2. **Signal Forms** en los cuatro formularios, con el mismo comportamiento visible.
3. **`provideAppInitializer()`** para el arranque de la sesión.

---

## Verificación y límites

**Leído directamente**: [angular.dev/style-guide](https://angular.dev/style-guide); [angular.dev/ai/develop-with-ai](https://angular.dev/ai/develop-with-ai); [best-practices.md](https://angular.dev/assets/context/best-practices.md) (resumen, la herramienta no devolvió el texto literal completo); el README y el árbol de `angular/skills`; `SKILL.md`, `naming-conventions.md` y `creating-services.md` de `angular-developer`; el `schema.json` y la plantilla del schematic `service` de `angular/angular-cli`; [angular-cli#30050](https://github.com/angular/angular-cli/pull/30050); la página de [skills.sh/angular](https://www.skills.sh/angular); `@angular/core` 22.2.1 en `frontend/node_modules`.

**No verificado**:
- El texto literal del anuncio de v20: la página devolvió 403. Su contenido sale del resultado de búsqueda, que cita el anuncio.
- Las cifras de installs cambian cada semana y no coinciden entre skills.sh y openskills.app.
- Que guards e interceptores usen guion en el estilo nuevo sale de la migración (`typeSeparator: '.'` para conservar el estilo viejo), no de un texto que lo diga.
- No leí el resto de las referencias de la skill (Signal Forms, router, testing). Se leen en el paso que las use.
