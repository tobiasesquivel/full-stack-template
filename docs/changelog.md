# Changelog

## 2026-10-15. Retiro de OpenSpec

- Se elimina `openspec/`. Los tests de integración por feature fijan los contratos HTTP. Las reglas Dune viven en `dune/` y se describen en `docs/architecture.md`.
- `check-structure` deja de exigir `openspec/specs` y `check-comments` deja de excluir su archivo histórico.
- Las entradas anteriores y las ADR conservan sus menciones a OpenSpec como historia.

## 2026-10-03. Contratos de la copia

- La fuente activa OpenSpec queda en la raíz con Auth, Users, Tasks, Health, Observability y Dune. Las cuatro specs antes activas del backend y su config se conservan bajo `openspec/changes/archive/pre-0003-backend-contract`. Los cinco cambios históricos conservan sus carpetas y contenido en el archivo raíz.
- `openspec/config.yaml` deja de describir MySQL, cleanapi, Wolverine y ausencia de tests. El [índice histórico](../openspec/changes/archive/README.md) registra qué contratos sustituyen cada documento anterior.
- ADR-0001 y ADR-0002 se trasladan a `docs/adr` con notas de supersesión parcial por ADR-0003. Sus argumentos y consecuencias originales permanecen como historia.
- `CONTEXT.md` se traslada a la raíz y contiene solo perfil, propietario, tarea, permiso, rol, sesión y familia. Las reglas de código quedan en arquitectura, convenciones y Dune.
- `docs/architecture.md`, `docs/conventions.md` y README sustituyen la lista de metas pendientes y las convenciones extensas del backend. Se retiran `backend/docs/project-goals.md`, `AspNetConventions.md` y el snapshot antiguo de Scalar.
- Se retira `backend/.template.config/template.json`. La distribución es un Git template del monorepo, no `dotnet new cleanapi`. Se incluyen frontend, backend, infraestructura y gates sin flags de muestra.

## Evidencia y límites

No se ejecutaron builds, tests, linters, formatters ni runtime durante este trabajo documental. El integrador informó que SDK 10.0.401 está disponible para su verificación posterior. Backend y analyzers están NOT VERIFIED en esta entrega. La implementación de ADR-0003 requiere evidencia de los gates y del flujo real antes de declarar terminado el template. Los reportes heredados conservados no prueban el estado de esta copia.

## 2026-10-05. Verificación real Compose + navegador

- Backend 24/24 pass (reporte parent artifact://611). Dune 22/22 PASS local (artifact://640); estructura 5/5 + check-structure OK + Angular build PASS. Gate duro real: EF DbContext en Domain throwaway → `dotnet build` backend exit 1 error DUNE0001 (`artifacts/compose-final/dune-negative-build.stdout.log`); throwaway removido y `dotnet build TemplateApp.slnx` exit 0, 0 errores (artifact://643). Docker smoke 49 PASS, API UID 1654. GitHub Actions no ejecutado; sin afirmación de cero warnings. Commits cierre: backend `b8c4236`, Angular `7ba6a4c`, CI `5363ec4`, helper `5cd9ca3`.
- Docker `up --build` final con éxito: `artifacts/compose-final/compose-export-fix.stdout.log` + `.stderr.log` (imágenes api/web construidas, `db` healthy, `api`/`otel` recreados e iniciados).
- Smoke `--compose` exit 0: `artifacts/compose-final/compose-smoke-fixed.stdout.log` (+ `.stderr.log`) con health live 200 y ready 200 en servicio; tras caída de `db`, live 200 y ready 503; reinicio con persistencia (tarea creada antes sigue legible); OTLP `correlatedLogs true` + `httpDurationMetric true`.
- Navegador PASS: `artifacts/compose-final/browser-result.json`, `browser-network.json` y `tasks.png` con register, create, dos reloads sin perder login, checkbox persiste, perfil persiste, logout y login.
- Backend Dockerfile usa `USER $APP_UID` (usuario `app` builtin, `adduser` ausente en .NET 10). OTel exporters usan base + `/v1/traces` y `/v1/metrics` porque el setter `Endpoint` no agrega ruta.
- Migración única `InitialCreate` generada por CLI en commit `6687af8`, sin edición manual de archivos; original no tocado. ADR en commit `55639b7`.
- Comandos corregidos en README: `node --test tools/check-structure.test.mjs` (path explícito), `dotnet test TemplateApp.slnx` con runner MTP (proyectos backend `Tests` y `tools/Dune.Analyzers.Tests`), sin suite frontend vigente (`frontend/**/*.spec.ts` ausente; `app.spec` eliminado incidental; `npm test` sin runner; prueba real en navegador) y smoke con flag `--compose`.

## 2026-10-05. Localidad de los módulos

- Dune separa el inventario Git de artefactos versionados de la detección de repositorios anidados. La caché local de Angular ya no provoca el fallo de artefactos versionados. Un archivo ignorado pero agregado al índice sí falla. El gate conserva la comprobación de artefactos borrados del directorio de trabajo pero aún indexados.
- Verificación de estructura con `node --test tools/check-structure.test.mjs` y `node tools/check-structure.mjs`. Las 11 pruebas pasan y el gate devuelve cero sin borrar la caché de Angular.
- `backend/Tests/Support/TestSession.cs` sustituye el protocolo HTTP copiado en Auth, Tasks y Users. Cada sesión conserva su cookie jar. Las expectativas de contrato permanecen junto a cada feature. Las 24 pruebas HTTP pasan con PostgreSQL real y el smoke devuelve cero en `artifacts/verify-template/architecture-support-20261005`.
- `SessionHttpPolicy` concentra cookies y antiforgery de Auth. Los endpoints ya no repiten guards y `TokenService` deja de conocer cookies HTTP. Las cuatro rutas POST conservan la sesión ante un rechazo CSRF. Las 28 pruebas backend pasan y el smoke HTTP devuelve cero en `artifacts/verify-template/architecture-session-20261005`.
- Tarea usa archivos por caso de uso con módulo y adapter HTTP colocados. Crear y actualizar incluyen su request y validator. El cliente y los modelos exclusivos Angular salen de Core. Se retiran los archivos y exports anteriores sin aliases. No cambian las rutas, payloads, estado final por PUT ni precedencia de validación y autorización.
- DUNE0002 identifica el tipo endpoint por símbolos en archivos mixtos y conserva la regla anterior por nombre de archivo. Las 24 pruebas de analyzers pasan. Un build real con EF dentro de `TasksEndpoints` en `GuardProbe.cs` falla con DUNE0002; el archivo temporal se retira antes del build final.
- Verificación final `architecture-final-20261005` con builds backend y Angular, 28/28 pruebas backend, smoke HTTP y navegador PASS. El navegador cubre edición, estado completado, paginación, borrado en límite de página, perfil, reload, logout y login. La recuperación de un click fuera de pantalla queda registrada. Solo se limpian recursos propios. No se ejecuta CI remoto ni se afirma cero warnings.

## 2026-10-06. Upgrade Dune verificado

- Analizadores 44/44 PASS (`dotnet test dune/Dune.Analyzers.Tests`), backend 28/28 PASS con PostgreSQL real, build `TemplateApp.slnx` 0 errores.
- Gates Node PASS: estructura 14/14 + OK, frontend 13/13 + OK, comentarios 12/12 + OK en `frontend/` y `dune/`, verificador C# exit 0.
- DUNE0001 cubre cuerpos (método/propiedad/lambda/local/typeof/genérico/alias) con spans distintos por parámetro; DUNE0009 reporta comentarios fuera de migraciones EF, con CLI `--fix`/`--dry-run` y exención exacta del directorio generado.
- AuthService usa señales privadas con vistas `asReadonly`; frontend rechaza HTTP/storage directo en componentes con aliases y shadowing cubiertos.
- Huellas EF intactas en `artifacts/dune-upgrade/ef-before.json`; `tools/` eliminado, política en `dune/Dune.props`, `verify.mjs` sin comentarios.
- Recorrido navegador/API: NOT_RUN. El helper levantó API+front (READY) pero los procesos hijos murieron antes del smoke (ECONNREFUSED en api/front/control); la evidencia queda en `artifacts/verify-template/dune-final*/`. No se afirma PASS runtime.
