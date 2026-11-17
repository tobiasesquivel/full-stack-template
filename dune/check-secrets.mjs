import { execFile } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { promisify } from 'node:util';

const run = promisify(execFile);

const GITLEAKS_VERSION = '8.30.1';

const INSTALL_HINT = `instalá gitleaks ${GITLEAKS_VERSION}: winget install --id Gitleaks.Gitleaks --version ${GITLEAKS_VERSION} --exact (o definí GITLEAKS_BIN)`;

const SCANS = [
  { name: 'historial alcanzable desde HEAD', args: ['git', '--log-opts=--full-history HEAD'] },
  { name: 'cambios sin preparar', args: ['git', '--pre-commit'] },
  { name: 'cambios preparados', args: ['git', '--pre-commit', '--staged'] },
];

function parseArgs(argv) {
  const flags = { root: process.cwd() };
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === '--root') {
      flags.root = path.resolve(argv[i + 1]);
      i += 1;
    } else {
      throw new Error(`opción desconocida: ${argv[i]}`);
    }
  }
  return flags;
}

const GIT_WITHOUT_CRLF_WARNING = { GIT_CONFIG_COUNT: '1', GIT_CONFIG_KEY_0: 'core.safecrlf', GIT_CONFIG_VALUE_0: 'false' };

async function gitleaks(bin, args, cwd) {
  try {
    const env = { ...process.env, ...GIT_WITHOUT_CRLF_WARNING };
    const { stdout, stderr } = await run(bin, args, { cwd, env, maxBuffer: 16 * 1024 * 1024 });
    return { code: 0, output: stdout + stderr };
  } catch (error) {
    if (error.code === 'ENOENT') throw new Error(`no encuentro "${bin}"; ${INSTALL_HINT}`);
    return { code: error.code, output: (error.stdout ?? '') + (error.stderr ?? '') };
  }
}

async function main() {
  const { root } = parseArgs(process.argv.slice(2));
  const config = path.join(root, '.gitleaks.toml');
  if (!existsSync(config)) throw new Error(`falta ${config}`);

  const bin = process.env.GITLEAKS_BIN ?? 'gitleaks';
  const version = (await gitleaks(bin, ['version'], root)).output.trim();
  if (version !== GITLEAKS_VERSION) throw new Error(`gitleaks ${version || '?'} en vez de ${GITLEAKS_VERSION}; ${INSTALL_HINT}`);

  const failures = [];
  for (const scan of SCANS) {
    const result = await gitleaks(bin, [...scan.args, root, '--config', config, '--redact', '--no-banner', '--no-color', '--verbose'], root);
    const errored = /\bERR\b/.test(result.output);
    const passed = result.code === 0 && !errored;
    if (!passed) {
      failures.push(scan.name);
      process.stdout.write(result.output);
    }
    console.log(`check-secrets: ${scan.name}: ${passed ? 'OK' : `FAIL (código ${result.code}${errored ? ', gitleaks informó un error' : ''})`}`);
  }

  if (failures.length > 0) {
    console.error(`check-secrets: FAIL en ${failures.join(', ')}. Un secreto real se revoca y se rota; un falso positivo va a .gitleaks.toml con description.`);
    process.exit(1);
  }
  console.log('check-secrets: OK');
}

main().catch((error) => {
  console.error(`check-secrets: ${error.message}`);
  process.exit(1);
});
