# Paso 13: escaneo continuo de secretos y dependencias (investigación)

- Fecha: 2026-11-17.
- Alcance: la mitad "escaneo continuo" del paso 13 del [plan](../plan.md) (`plan.md:326-329`). El plan pide cambiar `ci.yml` (escaneo de dependencias y de secretos) y `docs/`, y verificar que "el job nuevo falla ante un secreto plantado en un fixture de prueba y luego se retira". Las cabeceras de seguridad HTTP van en otro informe. Este no las trata.
- Decisiones que ya están tomadas y que este informe no reabre:
  - "No se versionan secretos ni connection strings con credenciales" (`docs/conventions.md:102`).
  - Elegir una herramienta, una librería o una licencia es de Tobi (regla 5 de `AGENTS.md`). En modo autónomo es la excepción que frena el hilo: "siempre que pregunten por elegir una herramienta, yo quiero responder" (sección "Modo autónomo" de `AGENTS.md`).
  - El paso 34 ("Cadena de suministro", `plan.md:444-447`) es dueño del escaneo de **imagen**, el SBOM CycloneDX, la procedencia, el versionado centralizado de paquetes y el **lockfile**, y de la demostración con "una dependencia vulnerable plantada a propósito en un fixture".
  - Un gate nuevo es el objetivo declarado del paso: se agrega con una línea en `docs/decisions.tsv` (regla 9) y, si es una regla nueva, con su fixture válida y su violación deliberada (regla 4, `docs/conventions.md:110`).
- Método: solo fuentes primarias: docs.github.com, Microsoft Learn (NuGet), docs.npmjs.com, los READMEs, LICENSE y código fuente de cada herramienta en su tag, el aviso GHSA de GitHub y OWASP. Cada afirmación lleva su link. Las licencias, versiones y fechas de release salen de la API de GitHub (`gh api repos/<dueño>/<repo>` y `.../releases/latest`, consultada el 2026-11-17). Lo que es deducción mía va marcado [INFERENCIA].
- Evidencia propia: experimentos descartables fuera del repo, en el scratchpad de la sesión. No se tocó ningún archivo del repo salvo este informe.
  - **E1**: gitleaks 8.30.1 para Windows (checksum SHA-256 verificado contra `gitleaks_8.30.1_checksums.txt` de la release) sobre el historial y el árbol de trabajo reales del repo.
  - **E2**: un clon temporal del repo con tres secretos falsos plantados en `backend/Tests/Fixtures/`, un commit que los agrega y otro que los retira. Escaneo con gitleaks en cuatro modos.
  - **E3**: TruffleHog 3.99.9 y Betterleaks 1.10.1 para Windows (checksums verificados) sobre el mismo clon y sobre el repo real.
  - **E4**: `npm audit` sobre `frontend/package-lock.json` real, con y sin `--omit=dev`, y sus códigos de salida.
  - **E5**: un proyecto `net10.0` descartable con `Newtonsoft.Json` 12.0.1 (aviso alto [GHSA-5crp-9r3c-p9vr](https://github.com/advisories/GHSA-5crp-9r3c-p9vr)) para ver cómo falla cada forma de auditoría NuGet. Después, la misma auditoría sobre `TemplateApp.slnx`.
  - **E6**: alcance del escaneo de historial (`--all` contra solo lo alcanzable desde `HEAD`) y una allowlist de prueba.

## Glosario corto

- **Secreto**: un valor que da acceso a algo (clave de API, token, contraseña). Si llega a git, cualquiera con acceso al repo lo puede leer, también en commits viejos.
- **Escáner de secretos**: un programa que busca en archivos o en el historial de git textos con forma de secreto (por ejemplo `AKIA` + 16 caracteres = una clave de acceso de AWS). Usa reglas: una regex más, a veces, un mínimo de **entropía** (cuánto se parece el texto a algo aleatorio).
- **Falso positivo**: el escáner marca algo que no es un secreto (un ejemplo de la documentación, una contraseña de prueba). **Allowlist**: la lista de excepciones que el escáner ignora.
- **Escaneo de dependencias** (SCA, *software composition analysis*): comparar las versiones de los paquetes que usa el proyecto contra una base de **avisos** (*advisories*) de vulnerabilidades conocidas. **Dependencia transitiva**: la que no pedís vos, sino un paquete que pediste.
- **GitHub Advisory Database**: la base pública de avisos de GitHub; cada aviso tiene un id `GHSA-...`. **OSV**: la base abierta de Google con el mismo propósito.
- **Lockfile**: archivo que fija la versión exacta de cada dependencia, también las transitivas. El frontend tiene uno (`frontend/package-lock.json`); el backend no (`packages.lock.json` no existe en el repo, comprobado con `git ls-files`).
- **Fijar por SHA**: escribir `uses: actions/checkout@<commit de 40 caracteres>` en vez de `@v4`. Un tag se puede mover; un commit no.

## Conclusión

- **Qué da GitHub gratis acá:** casi nada para este paso. El repo es privado y de una cuenta personal en el plan Free. Secret scanning y push protection piden GitHub Secret Protection en repos privados; dependency review (`dependency-review-action`) pide Code Security; subir SARIF a code scanning también. Lo gratis es el dependency graph y las **alertas de Dependabot**, que hoy están **apagadas** (la API devuelve 404 en `vulnerability-alerts`). Las alertas no hacen fallar un job. Entonces el gate del paso tiene que ser una herramienta que corra dentro del CI.
- **Secretos, mi recomendación: gitleaks 8.30.1 como binario** (MIT), bajado en el job con versión fija y checksum SHA-256, sin la `gitleaks-action`. En el repo real dio **un solo falso positivo** en todo el historial (E1): el ejemplo de `Idempotency-Key` de Stripe en `paso-09-idempotencia-escritura.md:75`. TruffleHog dio 7 y Betterleaks 35 (E3). Detecta una clave de AWS falsa aleatoria y un token `ghp_` falso (E2). Corre igual en Windows. La `gitleaks-action` tiene licencia propia (EULA), no permite modificarla, pide clave si el repo pasa a una organización y trae por defecto gitleaks 8.24.3. La contra: gitleaks está "feature complete" y solo recibirá parches de seguridad; su autor sigue en Betterleaks.
- **Dependencias, mi recomendación: las auditorías que ya vienen con .NET y npm**, sin herramienta nueva:
  - NuGet: `dotnet restore TemplateApp.slnx "-warnaserror:NU1903;NU1904"` (aviso alto y crítico = error). En `net10.0` NuGetAudit ya audita también las transitivas por defecto. Ojo: `dotnet package list --vulnerable` **sale con código 0 aunque encuentre un paquete vulnerable** (E5), entonces no sirve para fallar el job.
  - npm: `npm audit --omit=dev --audit-level=high` en `frontend/`.
  - Las dos usan la GitHub Advisory Database.
- **El repo hoy no pasa el gate de npm.** `@angular/forms` 22.2.1 depende en producción de `zod` 4.6.5, que tiene un aviso alto ([GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5), arreglado en 4.6.6). `npm audit --omit=dev --audit-level=high` sale con código 1 (E4). El paso tiene que subir `zod` en el lockfile (4.6.6 entra en el rango `^4.0.10` que pide `@angular/forms`) para empezar en verde. Sin `--omit=dev` hay 9 avisos (5 altos), todos en herramientas de desarrollo: quedan como pendiente. NuGet está limpio hoy (E5).
- **Dónde corre:** un job nuevo `security` en `ci.yml` con `permissions: contents: read`, `actions/checkout` fijado por SHA, `fetch-depth: 0` y `persist-credentials: false`. Las mismas tres comprobaciones entran en `check-all.mjs` para correr en Windows.
- **Qué parte del historial se escanea:** todo el historial **alcanzable desde `HEAD`** (`--log-opts="--full-history HEAD"`), no `--all`. Con `--all`, que es lo que gitleaks hace por defecto, una rama de demostración viva en GitHub pondría en rojo a todos los demás PR, porque `fetch-depth: 0` baja todas las ramas (E6).
- **Cómo se demuestra el rojo:** en una **rama descartable y un PR en borrador que no se mergea**. El commit planta una clave AWS falsa *aleatoria* en `backend/Tests/Fixtures/`. No sirve `AKIAIOSFODNN7EXAMPLE`, porque gitleaks ignora todo lo que termina en `EXAMPLE` (E2). Se guarda el link del job rojo, se cierra el PR y se borra la rama. No conviene plantar en la rama del paso: este repo mergea con merge commits, entonces el commit plantado quedaría para siempre en el historial de `main` y el gate quedaría rojo, salvo que se le agregue una excepción.
- **Regla 4:** un test `node --test` que arma un repo temporal, como ya hace `dune/check-structure.test.mjs`, y genera la clave falsa en el momento. Comprueba tres cosas: el archivo limpio pasa, la clave plantada falla y la allowlist del repo no la tapa. Ningún secreto falso queda versionado.
- **Paso 34 y pendientes:** al paso 34 van el escaneo de imágenes Docker, el lockfile NuGet (que habilitaría OSV-Scanner o Trivy para NuGet), el SBOM, Dependabot version updates y fijar por SHA las acciones de los jobs que ya existen. Quedan como pendientes: los avisos de las dependencias de desarrollo, un hook local antes del commit, un escaneo programado (`schedule`) y el choque entre `conventions.md:102` y las contraseñas de desarrollo que ya están versionadas.

## 1. Qué ofrece GitHub gratis en este repo

Datos del repo: `gh api repos/tobiasesquivel/full-stack-template` devuelve `"private": true`, dueño `"type": "User"` y `security_and_analysis: null`. `vulnerability-alerts` devuelve 404, o sea que las alertas de Dependabot están apagadas. `automated-security-fixes` devuelve `{"enabled": false}`.

| Función de GitHub | ¿Gratis en un repo privado de una cuenta Free? | Fuente |
|---|---|---|
| Secret scanning | **No.** Gratis solo en repos públicos. En privados de organización pide "GitHub Secret Protection enabled on GitHub Team or GitHub Enterprise Cloud". En repos de usuario solo figura para Enterprise Managed Users. | [About secret scanning](https://docs.github.com/en/code-security/secret-scanning/introduction/about-secret-scanning) |
| Push protection del repo | **No.** "Requires GitHub Secret Protection to be enabled." | [About push protection](https://docs.github.com/en/code-security/secret-scanning/introduction/about-push-protection) |
| Push protection para usuarios | **No aplica.** "Stops you from pushing secrets to public repositories". | [About push protection](https://docs.github.com/en/code-security/secret-scanning/introduction/about-push-protection) |
| Dependency graph y alertas de Dependabot | **Sí.** "Some of these are included in all plans, such as dependency graph and Dependabot alerts." El plan Free personal las lista. Se prenden en Settings → Advanced Security. | [About GitHub Advanced Security](https://docs.github.com/en/get-started/learning-about-github/about-github-advanced-security), [GitHub's plans](https://docs.github.com/en/get-started/learning-about-github/githubs-plans), [Configuring Dependabot alerts](https://docs.github.com/en/code-security/dependabot/dependabot-alerts/configuring-dependabot-alerts) |
| Dependabot security updates y version updates | Las páginas leídas no las atan a un plan pago. Security updates pide tener prendidos el dependency graph y las alertas. La tabla de ecosistemas da `nuget`, `npm`, `github-actions`, `docker` y `docker-compose` con soporte para repos privados. No lo comprobé prendiéndolo. | [About Dependabot security updates](https://docs.github.com/en/code-security/dependabot/dependabot-security-updates/about-dependabot-security-updates), [Supported ecosystems](https://docs.github.com/en/code-security/dependabot/ecosystems-supported-by-dependabot/supported-ecosystems-and-repositories) |
| `dependency-review-action` | **No.** "available for all public repositories" y para "private repositories that have GitHub Code Security or GitHub Advanced Security enabled". | [About dependency review](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/about-dependency-review) |
| Subir SARIF a code scanning | **No.** En privados "code scanning is available when GitHub Code Security features are enabled". | [Uploading a SARIF file](https://docs.github.com/en/code-security/code-scanning/integrating-with-code-scanning/uploading-a-sarif-file-to-github) |

Dos límites de Dependabot para este repo:

- Para NuGet, el dependency graph lee `.csproj` y no detecta las transitivas de forma estática; para eso hace falta "automatic dependency submission" ([Supported package ecosystems](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/dependency-graph-supported-package-ecosystems), [Automatic dependency submission](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/configuring-automatic-dependency-submission-for-your-repository)).
- "Dependabot will not create alerts for actions pinned to SHA values" ([Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use)).

Conclusión de la sección: el plan pide que "el job nuevo falla", y una alerta de Dependabot no hace fallar ningún job. El gate tiene que correr dentro del CI. Prender las alertas de Dependabot es un complemento gratis, sin archivo, y lo decide Tobi (sección 8).

## 2. Escáneres de secretos

### 2.1 Qué es cada uno

| | gitleaks | gitleaks-action | Betterleaks | TruffleHog | detect-secrets |
|---|---|---|---|---|---|
| Licencia | MIT (API de GitHub; [README](https://github.com/gitleaks/gitleaks)) | EULA propia desde v2.0.0; antes de v2 era MIT ([README](https://github.com/gitleaks/gitleaks-action), [LICENSE.txt](https://github.com/gitleaks/gitleaks-action/blob/master/LICENSE.txt)) | MIT ([README](https://github.com/betterleaks/betterleaks)) | AGPL-3.0: "Since v3.0, TruffleHog is released under a AGPL 3 license" ([README](https://github.com/trufflesecurity/trufflehog)) | Apache-2.0 ([README](https://github.com/Yelp/detect-secrets)) |
| Última versión | v8.30.1, 2026-03-21 | v3.0.0, 2026-05-30 | v1.10.1, 2026-11-12 | v3.99.9, 2026-11-12 | v1.5.0, 2024-05-06 |
| Mantenimiento | "feature complete"; de ahora en más solo parches de seguridad; el autor pasa a Betterleaks ([README](https://github.com/gitleaks/gitleaks)) | Activa; corre en Node 24 | Activo; "maintained by the folks who made Gitleaks, including the original author" | Activo | Sin release desde 2024-05 |
| Cómo corre en Actions | Binario de la release + `checksums.txt` | `uses: gitleaks/gitleaks-action@v3` | Binario + `checksums.txt` (con firma sigstore) | `uses: trufflesecurity/trufflehog@main` o binario | `pip install`; sin action oficial |
| Windows local | Binario `.exe` (E1); `winget install Gitleaks.Gitleaks` da 8.30.1 | No aplica | Binario `.exe` (E3); winget da 1.9.0, atrasado | Binario `.exe`; con un repo local hay que pasar `file://.` desde la carpeta del repo: una ruta absoluta de Windows falla (E3) | No figura como soportado |
| Historial o diff | `git` (historial, `--log-opts`), `git --pre-commit` (cambios sin commitear), `git --staged`, `dir` (archivos) | Rango de commits del PR (`--log-opts=--no-merges --first-parent base^..head`, [`src/gitleaks.js`](https://github.com/gitleaks/gitleaks-action/blob/v3.0.0/src/gitleaks.js)) | `git`, `filesystem` | `git`, `filesystem`; en la action, "Only code changes in the referenced commits are scanned" | Solo archivos actuales: "avoids the overhead of digging through all git history" |
| Excepciones | `.gitleaks.toml` (`[extend] useDefault = true`, `[[allowlists]]` con `paths`, `regexes`, `commits`), `.gitleaksignore` por huella (experimental), `gitleaks:allow` en la línea, `--baseline-path` | La misma config (`GITLEAKS_CONFIG`) | `betterleaks.toml`, `.betterleaksignore`; lee `GITLEAKS_CONFIG` | `trufflehog:ignore` en la línea | `.secrets.baseline`, `pragma: allowlist secret` |
| Verifica si el secreto anda | No | No | Opcional (`--validate`) | Sí: estados verified, unverified y unknown; por defecto informa los tres | No |
| Falla el proceso | Código 1 si encuentra algo (`--exit-code`) | Falla el job | Código 1 (`--exit-code`) | Código 183 con `--fail` | Hook con código distinto de 0 |

Detalles que pesan en este repo:

- **Licencia de la gitleaks-action.** Una cuenta personal no necesita clave: "then no License Key is required". Una organización sí: "you must obtain a License Key" ([LICENSE.txt](https://github.com/gitleaks/gitleaks-action/blob/master/LICENSE.txt)). La licencia también prohíbe modificarla: "You may not make any modifications to the original Software or source code". El código decide solo según el tipo de dueño: `is an individual user. No license key is required.` ([`src/index.js`](https://github.com/gitleaks/gitleaks-action/blob/v3.0.0/src/index.js)). Si no fijás `GITLEAKS_VERSION`, usa la versión que trae escrita en el código: `"8.24.3"` (mismo archivo, línea 138), cinco versiones menores atrás.
- **Alcance de `gitleaks git` por defecto.** Sin `--log-opts` corre `git log -p -U0 --full-history --all --diff-filter=tuxdb` ([`sources/git.go`](https://github.com/gitleaks/gitleaks/blob/v8.30.1/sources/git.go), líneas 93-94). `--all` incluye todas las ramas que haya en el clon.
- **La clave de ejemplo de AWS no sirve para plantar.** La regla `aws-access-token` es `\b((?:A3T[A-Z0-9]|AKIA|ASIA|ABIA|ACCA)[A-Z2-7]{16})\b` con `entropy = 3` y una allowlist `'''.+EXAMPLE$'''` ([`config/gitleaks.toml`](https://github.com/gitleaks/gitleaks/blob/v8.30.1/config/gitleaks.toml), líneas 205-220).
- **Quién controla la config.** gitleaks carga sola la `.gitleaks.toml` del repo escaneado ([README](https://github.com/gitleaks/gitleaks): orden `--config`, `GITLEAKS_CONFIG`, `GITLEAKS_CONFIG_TOML`, `.gitleaks.toml` del destino). Betterleaks, en cambio, solo la carga con `--unsafe-auto-load-config` (su `--help`). [INFERENCIA] Un PR puede agregar a la allowlist su propio secreto y el job pasa. La defensa es revisar cada cambio a `.gitleaks.toml` en el PR; el test de la regla 4 (sección 6.3) cubre que la allowlist no tape un secreto nuevo.

### 2.2 Qué encontraron en este repo (E1, E3)

| Herramienta | Historial del repo real | Qué marcó |
|---|---|---|
| gitleaks 8.30.1 | **1** hallazgo, 190 commits en menos de 1 s | `generic-api-key` en `docs/evolucion/investigacion/paso-09-idempotencia-escritura.md:75`: el texto `Idempotency-Key: KG5LxwFBepaKHyUD`, que es el ejemplo de la documentación de Stripe. Falso positivo. |
| TruffleHog 3.99.9 | **7**, todos `unverified` | Detector `SQLServer` sobre las connection strings con `Password=`: `backend/appsettings.json`, `backend/appsettings.Development.json`, `.github/workflows/ci.yml` (3), `ConfigurationValidationTests.cs`, `HealthEndpointsTests.cs`. |
| Betterleaks 1.10.1 | **35** | Regla `generic-password`: contraseñas de prueba en los tests de Auth, `ci.yml`, `.env.example` y los `appsettings*.json`, más el mismo caso de `paso-09`. |

gitleaks **no** marca `Password=123456` de `backend/appsettings.json:3` ni `Password=template-ci` de `ci.yml:37-44`. Para el gate eso es bueno: menos ruido. Pero también quiere decir que gitleaks no hace cumplir la mitad de `conventions.md:102` que habla de "connection strings con credenciales". Esos valores son contraseñas de desarrollo y de CI, y ya violan esa frase. Va como pendiente (sección 7).

### 2.3 ¿Detectan un secreto falso plantado? (E2, E3)

Plantado en `backend/Tests/Fixtures/` de un clon temporal:

| Valor plantado | gitleaks | TruffleHog | Betterleaks |
|---|---|---|---|
| `AKIA` + 16 caracteres aleatorios, más un `aws_secret_access_key` aleatorio de 40 | Sí (`aws-access-token` y `generic-api-key`) | Sí (`AWS`, `verified=false`) | Sí |
| `AKIAIOSFODNN7EXAMPLE` (la clave de ejemplo de AWS) | **No** (allowlist `.+EXAMPLE$`) | No figura entre los hallazgos | No figura |
| `ghp_` + 36 caracteres aleatorios | Sí (`github-pat`) | Sí (`Github`, `verified=false`) | Sí |

TruffleHog encuentra el secreto falso solo porque por defecto informa también los no verificados. Con `--results=verified` el plantado **no** haría fallar el job. Además, para verificar, TruffleHog prueba el valor contra la API del proveedor ("Credential confirmed as valid and active by API testing", [README](https://github.com/trufflesecurity/trufflehog)).

## 3. Historial o diff: cómo se ve el rojo y cómo se retira

Lo que pasó en E2 con gitleaks. El clon tiene el commit `plant` (agrega los secretos) y encima el commit `remove` (los borra):

| Modo | Código de salida | Qué significa |
|---|---|---|
| `gitleaks dir backend/Tests/Fixtures` con los archivos presentes | 1 | El árbol de trabajo tiene el secreto. |
| `gitleaks git .` (historial completo, `--all`) después de retirar | **1** | El secreto **sigue en el historial**. Borrar el archivo no alcanza. |
| `--log-opts="<plant>..HEAD"` (solo el commit que retira) | 0 | gitleaks mira las líneas agregadas (`git log -p`), y el commit que borra no agrega nada. |
| `--log-opts="<base>..HEAD"` (todo el PR) | **1** | El PR entero incluye el commit `plant`: sigue rojo aunque el último commit lo retire. |
| `--log-opts="--full-history HEAD"` en una rama sin el plant, con el plant vivo en otra rama (E6) | 1 solo por el falso positivo de `paso-09`; el plant no aparece | Sin `--all`, otra rama no contamina el escaneo. |
| Lo mismo con una allowlist para el ejemplo de Stripe (E6) | **0** en la rama limpia; **1** en la rama con `plant` y `remove` | La allowlist saca el falso positivo y no tapa el plant. |

Qué quiere decir para el paso:

- **Plantar en la rama del paso no funciona bien.** El PR queda rojo mientras el commit `plant` esté en su rango. Además, el repo mergea con merge commits (`allow_merge_commit: true`; los últimos merges de `main` son "Merge pull request #66", `#65` y `#64`), así que el commit llegaría a `main`. Desde ahí, cualquier escaneo del historial queda rojo para siempre, salvo que se agregue una excepción por huella (`commit:archivo:regla:línea` en `.gitleaksignore`) o se reescriba el historial. OWASP advierte que reescribir el historial "will break any other links to a given commit" ([OWASP Secrets Management, 9.2](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)).
- **Una rama descartable sí funciona.** Rama `fase4/13-demo-secreto` y PR en borrador → CI rojo → guardar el link del job → cerrar el PR sin mergear → borrar la rama. `main` nunca ve el commit. Como el escaneo es "desde `HEAD`" y no `--all`, la rama viva no pone en rojo a los otros PR mientras dure la demo.
- **Un secreto real no se arregla así.** Si alguna vez entra un secreto real, primero se revoca y se rota: "Keys that were exposed should undergo immediate revocation" ([OWASP, 9.2](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)). Sin push protection, el secreto ya está en GitHub cuando el CI lo detecta: el job avisa, no previene.

Escaneo completo o solo el diff:

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Historial alcanzable desde `HEAD` en cada corrida (`--log-opts="--full-history HEAD"`) | Un solo comando para PR y para `push` a `main`; también ve lo viejo; tarda menos de 1 s en este repo | Un falso positivo histórico necesita una excepción (hoy hay uno) |
| B. Solo el rango del PR o del push (lo que hace la gitleaks-action) | Avisa solo lo nuevo | Hay que calcular el rango para cada evento (`pull_request`, `push`, el primer push de una rama); lo viejo nunca se revisa |
| C. `gitleaks git` sin opciones (`--all`) | Lo más simple | Cualquier rama del remoto, como la de la demo, contamina todos los PR |

Recomiendo **A**.

## 4. Escaneo de dependencias

### 4.1 Opciones

| | NuGetAudit (viene con el SDK) | `npm audit` (viene con npm) | OSV-Scanner | Trivy `fs` | Dependabot alerts | `dependency-review-action` |
|---|---|---|---|---|---|---|
| Licencia | Parte del SDK de .NET | Parte de npm | Apache-2.0 | Apache-2.0 | Servicio de GitHub | MIT |
| Versión | SDK 10.0.401 (`global.json`) | npm del Node 26.7.0 de CI | v2.7.1, 2026-11-09 | v0.76.2, 2026-11-05 | — | v5.0.0 |
| NuGet | Sí, directas y transitivas (`NuGetAuditMode=all` por defecto en `net10.0`) | — | Solo con `packages.lock.json`, `packages.config` o `deps.json`; no lee `.csproj` | Solo con `packages.lock.json`, `*.deps.json` o `packages.config`; no lee `.csproj` | Lee `.csproj`; sin transitivas estáticas | — |
| npm | — | Sí, desde el lockfile | Sí (`package-lock.json`) | Sí | Sí | — |
| Cómo falla un job | Con `-warnaserror:NU1903;NU1904` el restore sale con código 1 (E5) | `--audit-level` = "The minimum level of vulnerability for `npm audit` to exit with a non-zero exit code" | `fail-on-vuln` (por defecto `true`) | Código de salida configurable | **No falla jobs** | Falla el check, pero no está disponible acá |
| Datos | GitHub Advisory Database | Endpoint `/-/npm/v1/security/advisories/bulk` del registry; los avisos que imprime son `GHSA-...` (E4) | OSV | Base propia de Trivy | GitHub Advisory Database | GitHub Advisory Database |
| Fuente | [Learn, Auditing packages](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) | [npm audit](https://docs.npmjs.com/cli/v11/commands/npm-audit) | [Lockfiles](https://google.github.io/osv-scanner/supported-languages-and-lockfiles/), [Action](https://google.github.io/osv-scanner/github-action/) | [Trivy .NET](https://trivy.dev/latest/docs/coverage/language/dotnet/) | Sección 1 | Sección 1 |

### 4.2 NuGetAudit en detalle

- **Desde cuándo:** NuGetAudit existe para PackageReference desde NuGet 6.8 / .NET 8 SDK. NuGet 7.0 / .NET 10 SDK cambia el modo por defecto. `NuGetAudit` vale `true` por defecto, `NuGetAuditLevel` vale `low`, y "`NuGetAuditMode` defaults to `all` when a project targets `net10.0` or higher. Otherwise `NuGetAuditMode` defaults to `direct`" ([Learn](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)).
- **En el repo:** `TemplateApp`, `TemplateApp.Tests`, `Dune.Analyzers.Tests` y `Dune.CommentVerifier` son `net10.0`, entonces auditan también las transitivas. `Dune.Analyzers` es `netstandard2.0` (`dune/Dune.Analyzers/Dune.Analyzers.csproj:4`), entonces solo audita sus dependencias directas.
- **Ya corre hoy, pero no frena nada.** Cada `dotnet build` del CI hace el restore y la auditoría. Los avisos salen como warnings `NU1901` a `NU1904` (bajo, moderado, alto, crítico). `dune/Dune.props:4` usa `TreatWarningsAsErrors=false` y su lista `WarningsAsErrors` no incluye los `NU19xx`. Entonces un paquete vulnerable deja el build verde. E5 lo muestra: `dotnet build` con `Newtonsoft.Json` 12.0.1 imprime `warning NU1903` y sale con código 0.
- **Cómo hacer que falle.** Microsoft da dos caminos:
  - Agregar los códigos a `WarningsAsErrors`: "if you want to keep low and moderate vulnerabilities as warnings, but treat high and critical vulnerabilities as errors ... `<WarningsAsErrors>$(WarningsAsErrors);NU1903;NU1904</WarningsAsErrors>`".
  - Un pipeline de auditoría aparte, porque así se evita que "new security advisories [block] your bug fixes at inconvenient times" ([Learn, Running NuGet Audit in CI](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)).
- **Qué probé (E5):**

| Comando | Código de salida |
|---|---|
| `dotnet build` (por defecto) | 0, con `warning NU1903` |
| `dotnet restore --force "-warnaserror:NU1903;NU1904"` | **1**, `error NU1903` |
| `dotnet restore --force "-p:WarningsAsErrors=NU1903%3BNU1904"` | **1**, pero reemplaza la lista `WarningsAsErrors` de `Dune.props` en esa corrida |
| `dotnet package list --vulnerable --include-transitive` | **0**, aunque lista el paquete con severidad `High` |
| Lo mismo con `--format json` | **0** |
| `dotnet restore TemplateApp.slnx --force "-warnaserror:NU1901;NU1902;NU1903;NU1904"` sobre el repo real | 0: hoy no hay avisos NuGet |

- **Por qué `--force` en local:** un restore que no tiene nada que hacer puede saltear proyectos. Microsoft sugiere contar `RestoreSkippedCount` "to account for projects that restore skipped because they were already up to date" ([Learn](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)). [INFERENCIA] Un proyecto salteado no se audita. En CI el clon siempre es nuevo, entonces no hace falta.
- **Excepciones:** `<NuGetAuditSuppress Include="https://github.com/advisories/XXXX" />`, "a last resort" (misma página).

### 4.3 `npm audit` en detalle y estado actual del frontend (E4)

- `--audit-level` "does not filter the report output, it simply changes the command's failure threshold". `--omit=dev` saca esas dependencias del pedido. npm "requires a package-lock or shrinkwrap" ([npm audit](https://docs.npmjs.com/cli/v11/commands/npm-audit)). En E4 corrió sin `node_modules`, así que en el CI no necesita `npm ci` antes.
- Hoy:

| Comando | Resultado | Código de salida |
|---|---|---|
| `npm audit` | 9 avisos: 1 bajo, 3 moderados, 5 altos | — |
| `npm audit --audit-level=high` | — | 1 |
| `npm audit --audit-level=critical` | — | 0 |
| `npm audit --omit=dev` | **1 alto: `zod` 4.6.5** ([GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5), `<4.6.6`) | 1 con `--audit-level=high` |

- De dónde viene `zod` en producción: `package-lock.json` muestra que `node_modules/@angular/forms` (22.2.1) declara `"zod": "^4.0.10"` en `dependencies`, y la entrada `node_modules/zod` (4.6.5) no tiene `dev: true`. Existen 4.6.6 y 4.6.7 (`npm view zod`). Subirla toca solo el lockfile.
- Los otros 8 avisos son de desarrollo: `vite` (vía `@angular/build`), `@vitest/mocker` y `vitest`, `@modelcontextprotocol/server` (vía `@angular/cli`), `undici`, `fast-uri` y `launch-editor`. Varios piden subir `@angular/build` o `@angular/cli` a 22.2.7, "outside the stated dependency range" según el propio `npm audit`.
- **Qué umbral conviene** [INFERENCIA]: `--omit=dev` cubre lo que llega al bundle que sirve nginx. Las dependencias de desarrollo corren en el CI y en la máquina del desarrollador, entonces también son riesgo de cadena de suministro, que es el tema del paso 34. Exigirlas hoy pone el gate en rojo por 8 avisos que no dependen del paso.

### 4.4 Trivy y la acción comprometida

En marzo de 2026, un atacante movió 76 de los 77 tags de `aquasecurity/trivy-action` y los 7 de `aquasecurity/setup-trivy` hacia commits maliciosos, y publicó un binario Trivy 0.69.4 malicioso. El código "Dumps `Runner.Worker` process memory ... to extract secrets". El aviso pide "Pin GitHub Actions to full, immutable commit SHA hashes, don't use mutable version tags" y rotar todos los secretos expuestos ([GHSA-69fq-xp46-6x23, CVE-2026-33634](https://github.com/advisories/GHSA-69fq-xp46-6x23)). Una herramienta de seguridad también es cadena de suministro. Por eso la recomendación baja el binario con versión y checksum fijos y fija las acciones por SHA.

## 5. Endurecer el workflow

- **Fijar por SHA:** "A tag can be moved or deleted if a bad actor gains access to the repository storing the action". Fijar por SHA "helps mitigate the risk of a bad actor adding a backdoor to the action's repository" ([Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use)). Hoy `ci.yml` usa tags (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/setup-node@v4`, `actions/upload-artifact@v4`). SHAs vigentes, consultados con `gh api repos/<acción>/git/ref/tags/<tag>`:
  - `actions/checkout` v4.4.0 = `11d5960a326750d5838078e36cf38b85af677262`.
  - `actions/setup-dotnet` v4.3.1 = `67a3573c9a986a3f9c594539f4ab511d57bb3ce9`.
  - `actions/setup-node` v4.4.0 = `49933ea5288caeca8642d1e84afbd3f7d6820020`.
- **Mínimo privilegio:** "set the default permission for the `GITHUB_TOKEN` to read access only for repository contents" ([Secure use](https://docs.github.com/en/actions/reference/security/secure-use)). Si declarás un permiso, "all of those that are not specified are set to `none`" ([Workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)). Hoy `ci.yml` no tiene bloque `permissions`. Para el job nuevo alcanza `contents: read`.
- **Checkout:** `fetch-depth` vale `1` por defecto; con `0` baja "all history for all branches and tags". `persist-credentials` vale `true` por defecto, y con `false` el token no queda en la config de git ([actions/checkout](https://github.com/actions/checkout)). El escáner no necesita el token.
- **Binario con checksum:** `gitleaks_8.30.1_linux_x64.tar.gz` = `551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb` (sale de `gitleaks_8.30.1_checksums.txt` de la release). El paquete de Windows trae `LICENSE`, `README.md` y el ejecutable (E1). [INFERENCIA] El de Linux tiene la misma forma; no lo abrí.

## 6. Propuesta para este repo

### 6.1 Job nuevo en `ci.yml`

Boceto con las opciones recomendadas. Los nombres son ilustrativos y los SHA son los de la sección 5:

```yaml
  security:
    name: Escaneo de secretos y dependencias
    runs-on: ubuntu-latest
    permissions:
      contents: read
    env:
      GITLEAKS_VERSION: 8.30.1
      GITLEAKS_SHA256: 551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0
        with:
          fetch-depth: 0
          persist-credentials: false
      - name: Instalar gitleaks (versión y checksum fijos)
        run: |
          curl -fsSLo gitleaks.tar.gz "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/gitleaks_${GITLEAKS_VERSION}_linux_x64.tar.gz"
          echo "${GITLEAKS_SHA256}  gitleaks.tar.gz" | sha256sum -c -
          tar -xzf gitleaks.tar.gz gitleaks
      - name: Secretos en el historial alcanzable desde HEAD
        run: ./gitleaks git . --redact --no-banner --log-opts="--full-history HEAD"
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with:
          dotnet-version: 10.0.401
      - name: Auditoría NuGet (alta y crítica fallan)
        run: dotnet restore TemplateApp.slnx "-warnaserror:NU1903;NU1904"
      - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020 # v4.4.0
        with:
          node-version: 26.7.0
      - name: Auditoría npm de producción (alta y crítica fallan)
        working-directory: frontend
        run: npm audit --omit=dev --audit-level=high
```

Por qué es un job aparte y no un paso más del job `backend`:

- Si falla, se ve en el nombre qué falló.
- Una vulnerabilidad publicada hoy pone rojo este job y no el build. Es lo mismo que recomienda Microsoft con "a dedicated pipeline" ([Learn](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)).

Con un PR, `actions/checkout` baja el merge commit `refs/pull/N/merge`. Entonces "desde `HEAD`" cubre la historia de `main` y los commits del PR.

`.gitleaks.toml` en la raíz, la que probé en E6:

```toml
[extend]
useDefault = true

[[allowlists]]
description = "Ejemplo de Idempotency-Key de la documentación de Stripe citado en paso-09"
regexes = ['''KG5LxwFBepaKHyUD''']
```

La recomiendo antes que `.gitleaksignore` por tres razones:

- La regex no depende del número de línea ni del commit.
- El README marca `.gitleaksignore` como experimental.
- La regex documenta el porqué en el mismo archivo.

### 6.2 En local (`check-all.mjs`, Windows)

`check-all.mjs` corre hoy 13 gates en orden (`.agents/skills/verify-template/scripts/check-all.mjs:62-76`). Propongo tres etapas más con los mismos comandos del CI:

- `secret-scan`: `gitleaks git . --redact --no-banner --log-opts="--full-history HEAD"`, y además `gitleaks git --pre-commit .`, que mira los cambios sin commitear. E2 lo probó: un cambio sin commitear con una clave falsa da código 1, y el árbol limpio da 0.
- `nuget-audit`: `dotnet restore TemplateApp.slnx --force "-warnaserror:NU1903;NU1904"`.
- `npm-audit`: `npm audit --omit=dev --audit-level=high` con `cwd: FRONTEND_DIR`, usando el helper `npm()` que ya maneja `cmd.exe` en Windows.

Tres cuidados:

- **En local no hay que usar `gitleaks dir .`.** Lee también los archivos que git ignora. E6 lo mostró: un secreto falso en `artifacts/` (carpeta ignorada en `.gitignore`) aparece como hallazgo, y `check-all` escribe sus logs justamente en `artifacts/`.
- **No conviene correr gitleaks con Docker en local.** El repo se usa en worktrees, donde `.git` es un archivo que apunta al repo principal con una ruta de Windows. [INFERENCIA] Esa ruta no existe dentro del contenedor, entonces el modo `git` no funcionaría.
- **Prerrequisito:** `winget install Gitleaks.Gitleaks` (8.30.1, la misma versión). Si `gitleaks` no está en el PATH, la etapa debería dar `FAIL` con ese mensaje, y el README debería listar el prerrequisito.

### 6.3 Regla 4 (Dune)

"Toda regla nueva se hace ejecutable en Dune, con una fixture válida y una violación deliberada", y las reglas de Dune se prueban "observando diagnósticos o códigos de salida" (`docs/conventions.md:110`). La regla `conventions.md:102` ya existía. Lo nuevo es su forma ejecutable: la config `.gitleaks.toml`. Un error en la allowlist (una regex o un `paths` demasiado amplio) apagaría el gate sin avisar. Propongo un test `node --test` en `dune/`, con el mismo patrón que `dune/check-structure.test.mjs:14-25`, que arma un repo temporal con `mkdtemp` y `git`:

- Un archivo limpio da código 0.
- Una clave `AKIA` aleatoria, generada en el test, da código 1 **con la `.gitleaks.toml` del repo**.
- La línea de Stripe de la allowlist da código 0.

Así la violación deliberada nunca se versiona. No hace falta un analyzer Roslyn ni una regla de `check-structure.mjs`: no hay estructura de código que mirar. Este test necesita el binario, entonces corre en el job `security` y en la etapa `secret-scan` de `check-all`.

### 6.4 Demostración del "Verifica" del plan

1. Local: con la rama del paso limpia, `check-all` deja `secret-scan` en `PASS`.
2. Rama descartable `fase4/13-demo-secreto` que sale de la rama del paso. Un commit agrega `backend/Tests/Fixtures/planted-secret.txt` con una clave `AKIA` aleatoria (no la de ejemplo de AWS). PR en borrador.
3. CI: el job `security` falla en "Secretos en el historial alcanzable desde HEAD", con `aws-access-token` y el valor tapado por `--redact`. Se guarda el link del job y el log.
4. Se cierra el PR sin mergear y se borra la rama en GitHub y en local. El job de la rama del paso sigue verde.
5. En `progreso.md` se registran el link del job rojo, el del verde y la frase "la clave era falsa y aleatoria, nunca válida".

### 6.5 Docs que cambian

- `docs/conventions.md:102`: agregar que gitleaks la hace cumplir en CI y en `check-all`, y que un falso positivo se resuelve con una entrada en `.gitleaks.toml` que tenga `description`, nunca con `gitleaks:allow` suelto. Agregar también que una dependencia de producción con aviso alto o crítico frena el CI.
- `README.md`, sección "Ejecutar gates" (`README.md:71-96`): los comandos nuevos y el prerrequisito `gitleaks`.
- `docs/decisions.tsv`: una línea por decisión: herramienta de secretos, herramienta de dependencias, umbral alto/crítico, `--omit=dev`, alcance "desde `HEAD`" y demostración en rama descartable (regla 9).
- `docs/architecture.md`: no hace falta. El cambio es del CI, no de la arquitectura de la app.

## 7. Qué entra en el paso 13, qué va al 34 y qué queda pendiente

| Tema | Dónde | Por qué |
|---|---|---|
| Escaneo de secretos en CI y en local | Paso 13 | Lo pide el plan (`plan.md:328`). |
| NuGetAudit y `npm audit` como gate | Paso 13 | "escaneo de dependencias" (`plan.md:328`); sin herramienta nueva. |
| Subir `zod` a 4.6.6 o más en el lockfile | Paso 13 | Sin eso, el gate nuevo nace en rojo. |
| Fijar por SHA y `permissions` del job nuevo | Paso 13 | Es parte de hacer bien el job nuevo (sección 4.4). |
| Escaneo de imágenes Docker (`postgres:18`, `nginx:1.28-alpine`, `node:26-bookworm-slim`, collector OTel) | Paso 34 | "escaneo de imagen" (`plan.md:446`). |
| Lockfile NuGet (`RestorePackagesWithLockFile`), `Directory.Packages.props`, y con eso OSV-Scanner o Trivy para NuGet | Paso 34 | "versionado centralizado de paquetes y lockfile" (`plan.md:446`). Sin lockfile, ninguno de los dos lee el backend. |
| SBOM CycloneDX y procedencia | Paso 34 | `plan.md:446`. |
| Dependencia vulnerable plantada en un fixture | Paso 34 | Es su "Verifica" (`plan.md:447`). |
| Fijar por SHA las acciones de los jobs que ya existen | Paso 34 o pendiente | Toca jobs fuera del paso (regla 7). |
| Dependabot version updates (`.github/dependabot.yml`) | Pendiente o paso 34 | Abre PRs solos; es una herramienta más que Tobi tiene que elegir. |
| Avisos de dependencias de desarrollo (8 hoy) | Pendiente | Piden subir `@angular/build`, `@angular/cli` y `vitest`; no dependen del paso. |
| Hook local antes del commit (`gitleaks git --staged` o `--pre-commit`) | Pendiente | OWASP: "Consider enabling secrets detection at the developer level" ([8.1](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)). Sin push protection, es lo único que frena antes del push. |
| Escaneo programado (`schedule`) para avisos nuevos en `main` sin PRs | Pendiente | Hoy `ci.yml` corre solo en `pull_request` y `push` a `main`. |
| `conventions.md:102` contra `Password=123456` (`backend/appsettings.json:3`, `appsettings.Development.json:9`) y `template-ci` (`ci.yml:20-44`) | Pendiente; lo decide Tobi | El doc dice que no se versionan connection strings con credenciales, y el código lo hace con contraseñas de desarrollo y de CI. gitleaks no las marca. |
| Alcance de "escaneo de dependencias" entre el paso 13 y el 34 | Para confirmar con Tobi | Los dos pasos lo nombran (`plan.md:328` y `plan.md:446`). Mi lectura: el 13 prende el gate con lo que ya viene; el 34 suma lockfile, SBOM e imágenes. |

## 8. Decisión de herramienta (la toma Tobi)

Por la regla 5 y la excepción del modo autónomo, el hilo frena acá y le pregunta a Tobi. Abajo están los datos y mi recomendación.

### 8.1 Escáner de secretos

| Opción | Qué ganás | Qué perdés | Licencia | Costo |
|---|---|---|---|---|
| **A. gitleaks 8.30.1, binario con checksum** (recomendada) | 1 falso positivo en todo el historial; menos de 1 s; `.exe` nativo y winget en la misma versión; config TOML con allowlist; sin token de GitHub | "feature complete": solo parches de seguridad; última release 2026-03-21; no verifica si el secreto anda; no marca connection strings | MIT | Gratis |
| B. `gitleaks/gitleaks-action` v3 | Comentarios en el PR, resumen y el rango calculado solo | EULA propia; prohíbe modificarla; pide clave si el repo pasa a una organización; trae 8.24.3 por defecto; usa `GITHUB_TOKEN`; otra acción de terceros en la cadena | EULA de gitleaks-action | Gratis en cuenta personal; con clave en una organización |
| C. Betterleaks 1.10.1 | Sucesor activo de los mismos autores; validación opcional; lee `GITLEAKS_CONFIG` | 35 falsos positivos acá con las reglas por defecto (`generic-password`): hay que afinarlo; proyecto joven; winget atrasado (1.9.0) | MIT | Gratis |
| D. TruffleHog 3.99.9 | Verifica contra el proveedor; muchos detectores; action oficial | 7 falsos positivos; con `--results=verified` no detecta el plantado falso; verificar manda el valor a APIs externas; detalle de rutas en Windows | AGPL-3.0 (como con Redis en el paso 33, Tobi decide si la acepta) | Gratis |
| E. detect-secrets 1.5.0 | Flujo con baseline; Apache-2.0 | No mira el historial; Python; sin release desde 2024-05; Windows no figura como soportado | Apache-2.0 | Gratis |
| F. Secret scanning de GitHub | Nativo, con push protection | **No disponible**: repo privado en plan Free | — | Pide GitHub Secret Protection |

Recomiendo **A**: menos ruido medido en este repo, licencia MIT sin condiciones, la misma versión en CI y en Windows, y sin acción de terceros con token. Si gitleaks deja de recibir parches, pasar a **C** es barato, porque Betterleaks lee la misma config por `GITLEAKS_CONFIG`, aunque habría que afinar sus reglas genéricas. Qué hace la industria: la documentación de GitHub empuja su secret scanning con push protection donde está disponible ([About secret scanning](https://docs.github.com/en/code-security/secret-scanning/introduction/about-secret-scanning)), y OWASP sugiere detectar en el puesto del desarrollador y combinar más de una herramienta ([OWASP 8.1](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)).

### 8.2 Escáner de dependencias

| Opción | Qué ganás | Qué perdés | Licencia | Costo |
|---|---|---|---|---|
| **A. NuGetAudit + `npm audit`** (recomendada) | Ninguna herramienta nueva; transitivas NuGet en `net10.0`; GitHub Advisory Database; mismos comandos en Windows; excepción NuGet por aviso (`NuGetAuditSuppress`) | Dos comandos; un aviso nuevo puede poner rojo un PR que no tocó dependencias; la página de `npm audit` que leí no documenta excepciones por aviso | SDK de .NET / npm | Gratis |
| B. OSV-Scanner 2.7.1 | Un solo escáner para los dos lados; base OSV; workflow que compara contra la rama base | Para NuGet pide `packages.lock.json`, que no existe (paso 34); su action sube SARIF por defecto, y eso no anda acá (`upload-sarif: false`); herramienta nueva | Apache-2.0 | Gratis |
| C. Trivy `fs` 0.76.2 | También escanea imágenes, útil para el paso 34 | Para NuGet pide `packages.lock.json`; su acción fue comprometida en marzo de 2026: solo como binario con checksum | Apache-2.0 | Gratis |
| D. Alertas de Dependabot (más security updates) | Gratis; sin archivo; avisa aunque no haya PRs | **No hace fallar un job**: no cumple el "Verifica"; para NuGet no ve transitivas estáticas; no alerta acciones fijadas por SHA; hoy está apagado | Servicio de GitHub | Gratis |
| E. `dependency-review-action` v5 | Bloquea el PR que agrega una dependencia vulnerable | **No disponible**: privado sin Code Security | MIT | Pide Code Security |

Recomiendo **A** como gate, porque cumple el paso sin sumar herramientas y cubre las transitivas NuGet sin lockfile. **D** es un complemento opcional: prenderlo es un clic en Settings, no deja archivos y avisa entre PRs. Lo decide Tobi. Qué hace la industria: Microsoft recomienda revisar la salida de `dotnet restore` "in your continuous integration process", con un pipeline de auditoría propio ([Learn](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)). OWASP pide "Continuously monitor sources like ... (CVE), (NVD), and (OSV)" y automatizarlo con herramientas de SCA ([OWASP Top 10 2025 A03](https://top10.owasp.org/2025/A03_2025-Software_Supply_Chain_Failures)).

### 8.3 Elecciones que no son de herramienta (modo autónomo: van con la recomendación)

- Umbral: alto y crítico fallan; bajo y moderado quedan como warning. Es el ejemplo de Microsoft (`NU1903;NU1904`).
- npm con `--omit=dev`; las dependencias de desarrollo quedan como pendiente.
- Escaneo del historial alcanzable desde `HEAD` en cada corrida.
- Demostración en una rama descartable que no se mergea.
- Falso positivo de `paso-09` resuelto con allowlist por regex en `.gitleaks.toml`.
- Test de la allowlist en `dune/` con un secreto generado en el momento.

## 9. Riesgos y lo que no comprobé

- No corrí el job en GitHub Actions. El boceto de la sección 6.1 sale de E1 a E6 en Windows, más la documentación.
- No abrí el paquete de Linux de gitleaks, y no probé `tar -xzf gitleaks.tar.gz gitleaks` en Linux.
- No prendí Dependabot. Que security updates y version updates sean gratis en este repo sale de leer las páginas, que no los atan a un plan pago. No lo vi funcionar.
- No probé el comando exacto para subir `zod` (por ejemplo `npm audit fix --omit=dev`). Solo comprobé que 4.6.6 existe y entra en el rango `^4.0.10`.
- Mi npm local es 12.2.0 con Node 26.7.0, y `package.json` declara `"packageManager": "npm@11.16.0"`. El CI usa el npm que trae Node 26.7.0. La página de `npm audit` que cité es la de v11; las opciones usadas existen en las dos versiones porque las corrí en 12.2.0.
- TruffleHog intentó verificar los valores falsos contra AWS y GitHub durante E3; dieron `verified=false`.
- Los avisos cambian todos los días. El estado de npm y NuGet de las secciones 4.2 y 4.3 es el del 2026-11-17.

## Fuentes

- GitHub Docs: [About secret scanning](https://docs.github.com/en/code-security/secret-scanning/introduction/about-secret-scanning), [About push protection](https://docs.github.com/en/code-security/secret-scanning/introduction/about-push-protection), [About GitHub Advanced Security](https://docs.github.com/en/get-started/learning-about-github/about-github-advanced-security), [GitHub's plans](https://docs.github.com/en/get-started/learning-about-github/githubs-plans), [About Dependabot alerts](https://docs.github.com/en/code-security/dependabot/dependabot-alerts/about-dependabot-alerts), [Configuring Dependabot alerts](https://docs.github.com/en/code-security/dependabot/dependabot-alerts/configuring-dependabot-alerts), [About Dependabot security updates](https://docs.github.com/en/code-security/dependabot/dependabot-security-updates/about-dependabot-security-updates), [About Dependabot version updates](https://docs.github.com/en/code-security/dependabot/dependabot-version-updates/about-dependabot-version-updates), [Supported ecosystems and repositories](https://docs.github.com/en/code-security/dependabot/ecosystems-supported-by-dependabot/supported-ecosystems-and-repositories), [Dependency graph supported package ecosystems](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/dependency-graph-supported-package-ecosystems), [Automatic dependency submission](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/configuring-automatic-dependency-submission-for-your-repository), [About dependency review](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/about-dependency-review), [Uploading a SARIF file](https://docs.github.com/en/code-security/code-scanning/integrating-with-code-scanning/uploading-a-sarif-file-to-github), [Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use), [Workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
- Microsoft Learn: [Auditing package dependencies for security vulnerabilities](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).
- npm: [npm audit (v11)](https://docs.npmjs.com/cli/v11/commands/npm-audit).
- Herramientas: [gitleaks](https://github.com/gitleaks/gitleaks), [`sources/git.go` v8.30.1](https://github.com/gitleaks/gitleaks/blob/v8.30.1/sources/git.go), [`config/gitleaks.toml` v8.30.1](https://github.com/gitleaks/gitleaks/blob/v8.30.1/config/gitleaks.toml), [gitleaks-action](https://github.com/gitleaks/gitleaks-action), [su LICENSE.txt](https://github.com/gitleaks/gitleaks-action/blob/master/LICENSE.txt), [`src/index.js` v3.0.0](https://github.com/gitleaks/gitleaks-action/blob/v3.0.0/src/index.js), [`src/gitleaks.js` v3.0.0](https://github.com/gitleaks/gitleaks-action/blob/v3.0.0/src/gitleaks.js), [Betterleaks](https://github.com/betterleaks/betterleaks), [TruffleHog](https://github.com/trufflesecurity/trufflehog), [detect-secrets](https://github.com/Yelp/detect-secrets), [OSV-Scanner: lockfiles](https://google.github.io/osv-scanner/supported-languages-and-lockfiles/), [OSV-Scanner: GitHub Action](https://google.github.io/osv-scanner/github-action/), [Trivy: .NET](https://trivy.dev/latest/docs/coverage/language/dotnet/), [actions/checkout](https://github.com/actions/checkout).
- Avisos: [GHSA-69fq-xp46-6x23 (Trivy)](https://github.com/advisories/GHSA-69fq-xp46-6x23), [GHSA-mrhx-c7xc-m3w5 (zod)](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5), [GHSA-5crp-9r3c-p9vr (Newtonsoft.Json, usado en E5)](https://github.com/advisories/GHSA-5crp-9r3c-p9vr).
- OWASP: [Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html), [Top 10 2025 A03 Software Supply Chain Failures](https://top10.owasp.org/2025/A03_2025-Software_Supply_Chain_Failures).
- Licencias, versiones y fechas de release: API de GitHub (`gh api repos/<dueño>/<repo>` y `.../releases/latest`), consultada el 2026-11-17.
