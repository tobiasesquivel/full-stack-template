# Mapa de verificación del template

Este mapa cubre las entradas reales del template. Lee el índice antes de conducir una instancia. Registra por separado cada entrada que no alcance su resultado.

## Condiciones de la instancia

Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs launch --run-id repro-template` desde el proyecto. El helper publica las URLs propias en `artifacts/verify-template/repro-template/run.json`. Usa esas URLs, no una instancia preexistente en 4200 u 8080. El PostgreSQL de la ejecución es descartable y separado de la base del desarrollador.

Un fallo de build, tests o arranque detiene el recorrido y conserva evidencia. Marca las etapas posteriores `NOT_RUN`. No reemplaces la API por un mock ni atribuyas el fallo a código que no ejecutaste.

## Convenciones de conducción

- Ejecuta `doctor --run-id repro-template` antes de conducir una instancia dudosa.
- Usa los nombres accesibles de campos, botones y enlaces que indican las recetas.
- Conduce la UI en un navegador real. Comprueba sus requests a `/api/v1` y la segunda lectura del dato guardado.
- Registra comando, stdout, stderr, código de retorno y resultado observable.
- Conserva las capturas y respuestas después de `cleanup`.

## Features

- [Health](./health.md) cubre proceso y dependencia de PostgreSQL.
- [Autenticación](./auth.md) cubre registro, login, restauración, rotación y cierre de sesión.
- [Tasks](./tasks.md) cubre lista paginada, creación, estado final, borrado y aislamiento de usuarios.
- [Perfil](./users.md) cubre identidad autenticada, edición y errores de autorización.
- [Runtime](./runtime.md) cubre build, tests, arranque de API y Angular, y fallos reproducibles.

## Estado de prueba

`PASS` exige que el recorrido real alcance sus aserciones. `FAIL` identifica una aserción o comando fallido con evidencia. `NOT_RUN` identifica una etapa que no se ejecutó. Un documento o una captura del scaffold no verifica una feature de negocio.
