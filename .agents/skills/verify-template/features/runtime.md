# Runtime

El desarrollador comprueba el backend compilado, sus tests, la API real y Angular conectado a esa API. Un fallo reproducido conserva el comando y su salida como resultado, sin presentar etapas omitidas como exitosas.

## Sub-features

- `runtime-build` ejecuta `dotnet build` sobre el proyecto real.
- `runtime-tests` ejecuta `dotnet test` sobre el proyecto de tests del backend.
- `runtime-api` arranca el backend compilado con PostgreSQL propio.
- `runtime-angular` sirve Angular con proxy a la API propia.
- `runtime-failure` conserva el primer fallo y limpia los recursos de la ejecución.

## How to get to it (user POV)

Desde la raíz del proyecto, ejecuta `node .agents/skills/verify-template/scripts/verify.mjs launch --run-id repro-template`. Para otro intento, usa otro run ID. Nunca sobreescribas evidencia previa.

## Driving it with verify-template

Preconditions:

- Node y npm están disponibles.
- El SDK que pide `global.json` está instalado.
- Docker tiene un motor disponible para PostgreSQL real.
- Ningún otro agente modifica las fuentes mientras ejecutas build y tests.

Launch registra el SDK y corre build del backend y tests. Usa los comandos registrados en `commands.jsonl` como verdad de esta ejecución. Un código de salida distinto de cero detiene esa etapa. Conserva stdout, stderr y `summary.json`. No cambies analyzers, severidades ni tests para silenciar un fallo.

Si build y tests pasan, el helper crea PostgreSQL descartable y arranca la API con secretos sintéticos de esa ejecución. Exige readiness 200 antes de servir Angular. El proxy generado apunta a la API real de ese run, no a un servidor de ejemplo.

Después de `VERIFY_TEMPLATE_READY`, ejecuta `doctor`, el comando `api` y una receta de navegador del mapa. Captura la URL propia, los metadatos de requests y la segunda lectura de un dato persistido. Un HTML 200 solo demuestra que Angular se sirve. El formulario debe operar contra la API para verificar el flujo full-stack.

Ejecuta `cleanup` en `finally`, también si una etapa falla. Confirma después que permanezcan `summary.json` y los logs. Si launch nunca quedó activo, cleanup debe informar que no tiene una instancia propia activa, sin matar procesos de otro run.

## Gotchas

- Build correcto no sustituye tests ni runtime.
- Tests sin ejecutarse no son un resultado verde.
- La ausencia del SDK, Docker o un proyecto se reporta con el comando que falló y sus etapas `NOT_RUN`.
- Una base vacía creada por el helper no sustituye las migraciones del producto.
- El primer fallo es evidencia. Una corrección posterior requiere otro run ID y conserva el repro original.
