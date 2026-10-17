---
name: verify-template
description: Verifica este template .NET y Angular en runtime real. Úsala para comprobar build y tests del backend, smoke de health/auth/tasks, UI contra la API o reproducir un fallo de arranque con evidencia.
---

# Verificar el template en runtime real

Esta skill pertenece al proyecto. Su producto es un flujo real verificado o el primer fallo reproducido con evidencia. Lee [el mapa de features](features/README.md) antes de conducir la aplicación.

## Launch

Trabaja desde la raíz del repo. Espera a que terminen las ediciones de backend y frontend antes del build.

Ejecuta en un terminal persistente:

```sh
node .agents/skills/verify-template/scripts/verify.mjs launch --run-id repro-template --front-build
```

`repro-template` es el ID de este ejemplo. Para otro intento, usa un ID nuevo y conserva el anterior. El helper registra prerequisitos, ejecuta `dotnet build` y `dotnet test`, crea PostgreSQL 18 aislado, arranca la API compilada y sirve Angular con proxy a esa API. Los secretos de la instancia son sintéticos y no usan la base del desarrollador.

La preparación termina únicamente cuando imprime `VERIFY_TEMPLATE_READY`. Lee las URLs y recursos propios en `artifacts/verify-template/repro-template/run.json`. Los puertos se asignan por ejecución. No conduzcas una instancia preexistente que coincida por nombre o puerto.

Si un comando falla, conserva `summary.json`, stdout, stderr y código de retorno. Registra las etapas posteriores como `NOT_RUN`. No cambies analyzers, tests, configuración de seguridad ni rutas para ocultar el fallo. El repro fallido es una entrega válida, pero no prueba que el template funcione.

## Doctor

Antes de conducir una instancia dudosa, ejecuta una sola comprobación de lectura:

```sh
node .agents/skills/verify-template/scripts/verify.mjs doctor --run-id repro-template
```

Exige identificación del owner de este run, API live y ready 200, y Angular servido. El canal de control usa una credencial local de la ejecución. Doctor no arregla ni reinicia procesos. Si falla, lee sus respuestas y logs antes de seguir.

## Drive

Primero prueba la interfaz HTTP real:

```sh
node .agents/skills/verify-template/scripts/verify.mjs api --run-id repro-template
```

El smoke usa cookies y antiforgery reales. Prueba health, registro y login, rotación y replay, revocación de sesión, perfil, Tasks, paginación y aislamiento de dos usuarios. No acepta un mock como sustituto de la API.

Después abre la URL `frontUrl` de `run.json` en un navegador real. Usa browser/CDP o Playwright disponible en el harness. Conduce [autenticación](features/auth.md), [Tasks](features/tasks.md) y [perfil](features/users.md) con sus labels e IDs de HTML. Como prueba mínima de UI, registra un usuario desde `/register`, crea una tarea desde `/tasks`, recarga y recupera el mismo dato. Observa las requests a `/api/v1` con respuestas reales.

El recorrido completo también existe como specs de Playwright Test en `frontend/e2e/` (`auth`, `session`, `tasks`, `profile` y `tabs`). Después de `api`, esperá 65 segundos para dejar libre la ventana del limiter de `/auth` y corré desde `frontend`:

```sh
E2E_BASE_URL=<frontUrl> E2E_CHANNEL=msedge E2E_OUTPUT_DIR=../artifacts/verify-template/repro-template/e2e npm run e2e
```

El reporte JSON, las capturas y los traces de las fallas quedan en `E2E_OUTPUT_DIR`. Una spec que falla es un `FAIL` del recorrido; no la saltees ni le subas el timeout para que pase.

Una UI servida con HTML 200 todavía no verifica negocio. Un build correcto tampoco verifica runtime. Exige la acción, su respuesta y una segunda lectura del dato persistido.

## Evidence

La evidencia vive en `artifacts/verify-template/<runId>/` y sobrevive al cleanup. El helper guarda comandos, cwd, timestamps, stdout, stderr, códigos de retorno, estado por etapa y recursos propios. `summary.json` conserva el primer fallo.

Guarda las capturas de UI, snapshots accesibles y metadatos de requests en la misma carpeta. Registra método, ruta, status y campo observable. Excluye contraseñas, tokens, Cookie, Set-Cookie y Authorization. No guardes HAR sin sanitizar.

Reporta por separado build, tests, arranque, smoke HTTP y UI. Usa `PASS` solo para el recorrido efectivamente ejercitado. Usa `FAIL` para un comando o aserción fallidos y `NOT_RUN` para lo omitido. `VERIFY_TEMPLATE_READY` prueba disponibilidad de las instancias, no el resultado del navegador.

El fallo de la base antes de iniciar la API es un fallo de startup. No lo presentes como readiness 503 observado. El fallo de compilación detiene la entrega con sus diagnósticos. Una corrección requiere otro run ID para conservar el repro original.

## Cleanup

Ejecuta siempre, también después de un fallo:

```sh
node .agents/skills/verify-template/scripts/verify.mjs cleanup --run-id repro-template
```

El helper pide al owner que cierre únicamente sus hijos, contenedor y volumen. No mates por nombre de proceso ni uses un prune global. Conserva los artefactos del repro. Si launch falló antes de crear owner, cleanup informa ese estado y mantiene la evidencia.

Después de cleanup, comprueba que `summary.json` y los logs sigan en la carpeta del run. Un cleanup que borra la prueba invalida esta skill.

## Helpers

- `scripts/verify.mjs` ejecuta `launch`, `doctor`, `api` y `cleanup`. Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs --help` para sus opciones y timeouts.
- `scripts/smoke-api.mjs` es la única fuente del smoke HTTP. El comando `api` lo ejecuta con la URL propia. Para un conjunto Compose creado por esta misma ejecución, el script también admite `--compose` y `OTEL_EXPORT_DIR`. Ese modo detiene y reinicia `db` y `api`; úsalo solo cuando poseas ese Compose completo.
- [Mapa de features](features/README.md) mantiene los recorridos y selectors de la UI.

Actualizá este mapa en el mismo PR que cambie rutas, contratos o controles visibles.
