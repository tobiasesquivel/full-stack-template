# Línea base

Estado del template antes del paso 2 del [plan](plan.md). Es la referencia para saber si un paso posterior rompió algo o si ya estaba roto.

**Conclusión**: todo lo que el paso 1 exige está en `PASS`. El único `NOT_RUN` es el navegador, que queda fuera del paso.

## Corrida

- Run ID: `baseline-20261016`.
- Fecha: 2026-10-16 (hora local; `summary.json` registra 2026-10-17T00:50Z en UTC).
- Commit: `88c5d39`, working tree limpio.
- Entorno: Windows 11, .NET SDK 10.0.401, Node v26.7.0, Docker 29.7.2, imagen `postgres:18`.
- Evidencia: `artifacts/verify-template/baseline-20261016/`. La carpeta está en `.gitignore`: vive solo en la máquina que corrió. Esta nota es el registro versionado.

## Gates del repo

| Gate | Comando | Estado | Resultado |
|---|---|---|---|
| Build | `dotnet build TemplateApp.slnx --configuration Release` | PASS | 0 errores |
| Tests | `dotnet test TemplateApp.slnx --configuration Release` | PASS | 72 de 72: 28 de `TemplateApp.Tests` y 44 de `Dune.Analyzers.Tests` |
| Comentarios C# | `dotnet run --project dune/Dune.CommentVerifier -- --root .` | PASS | Exit 0, sin violaciones `DUNE0009` |
| Estructura | `node dune/check-structure.mjs` + `node --test dune/check-structure.test.mjs` | PASS | `OK`; 14 de 14 tests |
| Comentarios (Node) | `node dune/check-comments.mjs --root .` + `node --test dune/check-comments.test.mjs` | PASS | `OK`; 12 de 12 tests |
| Frontend | `npm ci`, `npm run build`, `node dune/check-frontend.mjs --root .` | PASS | Build sin error; `check-frontend: OK` |

## Skill `verify-template`

| Etapa | Estado | Resultado |
|---|---|---|
| `launch` | PASS | prereqs, build, test (28/28), PostgreSQL, API, frontend y ready; imprimió `VERIFY_TEMPLATE_READY` |
| `doctor` | PASS | API live y ready 200; Angular sirve HTML 200 |
| `api` (smoke) | PASS | 45 requests reales: health, auth con rotación y replay, perfil, Tasks, paginación y aislamiento entre dos usuarios |
| Navegador | NOT_RUN | El paso 1 no lo exige. Sin evidencia de UI, no hay PASS de UI. |
| `cleanup` | PASS | Cerró API y frontend, borró su contenedor y volumen; la evidencia sigue en disco |

## Deuda observada, sin corregir

No son fallas: el build termina con 0 errores. Se registran para que un paso futuro no las confunda con regresiones.

- El build del backend reporta 245 advertencias de analyzers. Las más frecuentes: `CA2007` (230 líneas), `CA1515` (124), `CA1062` (66).
- `Dune.Analyzers` reporta `RS1036` y `RS2008`: falta `EnforceExtendedAnalyzerRules` y el seguimiento de versiones de las reglas.
- `Dune.Analyzers.Tests` reporta `MSB3277`: conflicto de versiones de `Microsoft.Bcl.AsyncInterfaces`.
- El conteo de advertencias depende del build incremental. Un build que no recompila un proyecto no repite sus advertencias. Para comparar, usá un build limpio.
