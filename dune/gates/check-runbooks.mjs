import { existsSync, globSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.join(here, '..', '..', 'frontend', 'package.json'));
const YAML = require('yaml');

const PROMETHEUS_CONFIG = 'deploy/prometheus/prometheus.yml';
const RUNBOOK_DIR = 'docs/operacion/runbooks';
const RUNBOOK_URL_PREFIX = `https://github.com/tobiasesquivel/full-stack-template/blob/main/${RUNBOOK_DIR}/`;
const SECTIONS = ['Significado', 'Impacto', 'Diagnóstico', 'Mitigación', 'Cómo probarla'];

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

function alertsIn(root, violations) {
  const configPath = path.join(root, PROMETHEUS_CONFIG);
  const config = YAML.parse(readFileSync(configPath, 'utf8')) ?? {};
  const alerts = [];
  for (const pattern of config.rule_files ?? []) {
    const ruleFiles = globSync(pattern, { cwd: path.dirname(configPath) });
    if (ruleFiles.length === 0) violations.push(['rule-file-missing', `${PROMETHEUS_CONFIG} carga ${pattern}, que no existe`]);
    for (const ruleFile of ruleFiles) {
      const groups = YAML.parse(readFileSync(path.resolve(path.dirname(configPath), ruleFile), 'utf8'))?.groups ?? [];
      const rules = groups.flatMap(group => group?.rules ?? []);
      for (const rule of rules.filter(entry => entry?.alert)) {
        alerts.push({ name: rule.alert, url: rule.annotations?.runbook_url, file: ruleFile });
      }
    }
  }
  return alerts;
}

function checkRunbook(root, name, violations) {
  const rel = `${RUNBOOK_DIR}/${name}.md`;
  const full = path.join(root, rel);
  if (!existsSync(full)) {
    violations.push(['runbook-missing', `${name}: falta ${rel}`]);
    return;
  }
  const lines = readFileSync(full, 'utf8').split(/\r?\n/);
  if (lines[0] !== `# ${name}`) violations.push(['runbook-title', `${rel}: la primera línea debe ser "# ${name}"`]);
  const headings = new Set(lines.filter(line => line.startsWith('## ')).map(line => line.slice(3).trim()));
  for (const section of SECTIONS.filter(section => !headings.has(section))) {
    violations.push(['runbook-section-missing', `${rel}: falta la sección "## ${section}"`]);
  }
}

function main() {
  const { root } = parseArgs(process.argv.slice(2));
  const violations = [];
  const alerts = alertsIn(root, violations);
  if (alerts.length === 0) violations.push(['no-alerts', `${PROMETHEUS_CONFIG} no carga ninguna regla de alerta`]);

  for (const alert of alerts) {
    const expected = `${RUNBOOK_URL_PREFIX}${alert.name}.md`;
    if (!alert.url) violations.push(['runbook-url-missing', `${alert.name} (${alert.file}): falta annotations.runbook_url`]);
    else if (alert.url !== expected) violations.push(['runbook-url-mismatch', `${alert.name} (${alert.file}): runbook_url debe ser ${expected}`]);
  }

  const names = new Set(alerts.map(alert => alert.name));
  for (const name of names) checkRunbook(root, name, violations);

  const runbookDir = path.join(root, RUNBOOK_DIR);
  const runbooks = existsSync(runbookDir) ? readdirSync(runbookDir).filter(file => file.endsWith('.md')) : [];
  for (const file of runbooks.filter(file => !names.has(file.slice(0, -3)))) {
    violations.push(['runbook-orphan', `${RUNBOOK_DIR}/${file}: ninguna alerta lo enlaza`]);
  }

  if (violations.length) {
    for (const [rule, detail] of violations) console.error(`${rule}: ${detail}`);
    console.error(`check-runbooks: FAIL (${violations.length} violaciones)`);
    process.exit(1);
  }
  console.log(`check-runbooks: OK (${names.size} alertas, ${runbooks.length} runbooks)`);
}

main();
