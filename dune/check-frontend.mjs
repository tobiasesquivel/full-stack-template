import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.join(here, '..', 'frontend', 'package.json'));
const ts = require('typescript');

export const RULES = ['FRONT001', 'FRONT002', 'FRONT003', 'FRONT004', 'FRONT005', 'FRONT006', 'FRONT007', 'FRONT008', 'FRONT009', 'FRONT010', 'FRONT011'];

const INITIALIZER_TOKENS = new Map([
  ['APP_INITIALIZER', 'provideAppInitializer'],
  ['ENVIRONMENT_INITIALIZER', 'provideEnvironmentInitializer'],
  ['PLATFORM_INITIALIZER', 'providePlatformInitializer'],
]);

const INLINE_VIEW_PROPERTIES = new Map([
  ['template', 'use templateUrl with its own file instead of inline template'],
  ['styles', 'style the template with Tailwind classes instead of inline styles'],
]);

const STYLE_FILE_PROPERTIES = new Set(['styleUrl', 'styleUrls']);

const LEGACY_FORMS_MODULE = 'external:@angular/forms';

const TYPE_SUFFIX_FILE = /\.(service|component|directive|pipe|guard|interceptor|resolver|module)\.ts$/;
const TYPE_SUFFIX_CLASS = /(Service|Component|Directive|Pipe|Module)$/;
const ANGULAR_CLASS_DECORATORS = new Set(['Component', 'Directive', 'Pipe', 'Service', 'Injectable', 'NgModule']);

const COMPOSITION_FILES = new Set([
  'frontend/src/app/app.routes.ts',
  'frontend/src/app/app.config.ts',
  'frontend/src/main.ts',
]);

const HTTP_DECLARATION = '@angular/common/http.d.ts';
const HTTP_SYMBOLS = new Set(['HttpClient', 'HttpRequest']);
const FETCH_SYMBOLS = new Set(['fetch', 'XMLHttpRequest']);
const STORAGE_NAMES = new Set(['localStorage', 'sessionStorage']);

function toPosix(value) {
  return value.split(path.sep).join('/');
}

function lineOf(sourceFile, pos) {
  return sourceFile.getLineAndCharacterOfPosition(pos).line + 1;
}

function unwrap(symbol, checker) {
  if (!symbol) return symbol;
  if (symbol.flags & ts.SymbolFlags.Alias) {
    try {
      return checker.getAliasedSymbol(symbol);
    } catch {
      return symbol;
    }
  }
  return symbol;
}

function declarationFile(symbol) {
  const declarations = symbol?.declarations;
  if (!declarations || declarations.length === 0) return null;
  return declarations[0].getSourceFile().fileName;
}

function isAngularHttp(symbol) {
  if (!symbol || !HTTP_SYMBOLS.has(symbol.name)) return false;
  const file = declarationFile(symbol);
  if (!file) return false;
  return toPosix(file).endsWith(HTTP_DECLARATION);
}

function isDomFetch(symbol) {
  if (!symbol || !FETCH_SYMBOLS.has(symbol.name)) return false;
  const file = declarationFile(symbol);
  if (!file) return false;
  return toPosix(file).endsWith('lib.dom.d.ts');
}

function resolvesToDomFetch(symbol, checker, seen = new Set()) {
  if (!symbol || seen.has(symbol)) return false;
  seen.add(symbol);
  if (isDomFetch(symbol)) return true;
  const declarations = symbol.declarations ?? [];
  for (const declaration of declarations) {
    if (ts.isVariableDeclaration(declaration) && declaration.initializer) {
      const init = declaration.initializer;
      if (ts.isIdentifier(init)) {
        const target = unwrap(checker.getSymbolAtLocation(init), checker);
        if (target && resolvesToDomFetch(target, checker, seen)) return true;
      } else if (ts.isPropertyAccessExpression(init) && ts.isIdentifier(init.expression)) {
        if (FETCH_SYMBOLS.has(init.name.text) && (init.expression.text === 'window' || init.expression.text === 'globalThis')) return true;
      }
    }
  }
  return false;
}

function isDomStorage(symbol) {
  if (!symbol || !STORAGE_NAMES.has(symbol.name)) return false;
  const file = declarationFile(symbol);
  if (!file) return false;
  return toPosix(file).endsWith('lib.dom.d.ts');
}

function decoratorName(expression) {
  if (ts.isCallExpression(expression) && ts.isIdentifier(expression.expression)) return expression.expression.text;
  if (ts.isIdentifier(expression)) return expression.text;
  return null;
}

function collectNaming(sourceFile) {
  const suffixedClasses = [];
  const rootInjectables = [];
  const initializerTokens = [];
  const inlineViews = [];
  const styleFiles = [];
  function visit(node) {
    if (ts.isImportDeclaration(node)
      && ts.isStringLiteral(node.moduleSpecifier)
      && node.moduleSpecifier.text === '@angular/core'
      && node.importClause?.namedBindings
      && ts.isNamedImports(node.importClause.namedBindings)) {
      for (const element of node.importClause.namedBindings.elements) {
        const imported = (element.propertyName ?? element.name).text;
        if (INITIALIZER_TOKENS.has(imported)) {
          initializerTokens.push({ line: lineOf(sourceFile, element.getStart()), token: imported });
        }
      }
    }
    if (ts.isClassDeclaration(node) && node.name) {
      const decorators = ts.getDecorators?.(node) ?? node.decorators ?? [];
      for (const decorator of decorators) {
        const name = decoratorName(decorator.expression);
        if (!name || !ANGULAR_CLASS_DECORATORS.has(name)) continue;
        if (TYPE_SUFFIX_CLASS.test(node.name.text)) {
          suffixedClasses.push({ line: lineOf(sourceFile, node.name.getStart()), name: node.name.text });
        }
        if (name === 'Component') {
          inlineViews.push(...componentProperties(sourceFile, decorator.expression, INLINE_VIEW_PROPERTIES));
          styleFiles.push(...componentProperties(sourceFile, decorator.expression, STYLE_FILE_PROPERTIES));
        }
        if (name === 'Injectable' && providesInRoot(decorator.expression)) {
          rootInjectables.push({ line: lineOf(sourceFile, decorator.getStart()), name: node.name.text });
        }
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return { suffixedClasses, rootInjectables, initializerTokens, inlineViews, styleFiles };
}

function exportedTypes(sourceFile) {
  return sourceFile.statements
    .filter((statement) =>
      (ts.isClassDeclaration(statement)
        || ts.isInterfaceDeclaration(statement)
        || ts.isTypeAliasDeclaration(statement)
        || ts.isEnumDeclaration(statement))
      && statement.name
      && (ts.getModifiers?.(statement) ?? statement.modifiers ?? []).some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword))
    .map((statement) => ({ line: lineOf(sourceFile, statement.name.getStart()), name: statement.name.text }));
}

function componentProperties(sourceFile, expression, names) {
  if (!ts.isCallExpression(expression)) return [];
  const options = expression.arguments[0];
  if (!options || !ts.isObjectLiteralExpression(options)) return [];
  return options.properties
    .filter((property) =>
      ts.isPropertyAssignment(property)
      && (ts.isIdentifier(property.name) || ts.isStringLiteral(property.name))
      && names.has(property.name.text))
    .map((property) => ({ line: lineOf(sourceFile, property.getStart()), property: property.name.text }));
}

function providesInRoot(expression) {
  if (!ts.isCallExpression(expression)) return false;
  const options = expression.arguments[0];
  if (!options || !ts.isObjectLiteralExpression(options)) return false;
  return options.properties.some((property) =>
    ts.isPropertyAssignment(property)
    && (ts.isIdentifier(property.name) || ts.isStringLiteral(property.name))
    && property.name.text === 'providedIn'
    && ts.isStringLiteral(property.initializer)
    && property.initializer.text === 'root');
}

function classDecoratorNames(node) {
  const decorators = ts.getDecorators?.(node) ?? node.decorators ?? [];
  return decorators.map((entry) => decoratorName(entry.expression)).filter(Boolean);
}

function classifyFile(rel, sourceFile) {
  if (COMPOSITION_FILES.has(rel)) return 'composition-root';
  const inCore = rel.startsWith('frontend/src/app/core/');
  const inFeature = rel.startsWith('frontend/src/app/features/');
  let component = false;
  let injectable = false;
  function visit(node) {
    if (ts.isClassDeclaration(node)) {
      for (const name of classDecoratorNames(node)) {
        if (name === 'Component') component = true;
        if (name === 'Injectable') injectable = true;
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  if (component) return 'component';
  if (inCore) return 'core';
  if (inFeature) return 'feature';
  return 'outside';
}

function isLooseFeatureFile(rel) {
  const feature = featureOf(rel);
  if (!feature) return false;
  const inside = rel.slice('frontend/src/app/features/'.length + feature.length + 1);
  return !inside.includes('/') && inside !== 'index.ts' && inside !== `${feature}.routes.ts`;
}

function featureOf(rel) {
  const marker = 'frontend/src/app/features/';
  if (!rel.startsWith(marker)) return null;
  return rel.slice(marker.length).split('/')[0] ?? null;
}

function isFeatureIndex(resolved, feature) {
  return resolved === `frontend/src/app/features/${feature}/index.ts`;
}

function collectEdges(sourceFile, rel, options, resolvedRoot) {
  const imports = [];
  const reexports = [];
  function record(specifier, dynamic, typeOnly, into) {
    const text = specifier.text;
    let resolved;
    if (text.startsWith('.')) {
      const result = ts.resolveModuleName(text, sourceFile.fileName, options, ts.sys);
      if (!result.resolvedModule) throw new Error(`${rel}:${lineOf(sourceFile, specifier.getStart())} cannot resolve ${text}`);
      resolved = toPosix(path.relative(resolvedRoot, result.resolvedModule.resolvedFileName));
    } else {
      const result = ts.resolveModuleName(text, sourceFile.fileName, options, ts.sys);
      if (result.resolvedModule && !result.resolvedModule.isExternalLibraryImport) {
        resolved = toPosix(path.relative(resolvedRoot, result.resolvedModule.resolvedFileName));
      } else {
        resolved = `external:${text}`;
      }
    }
    into.push({ from: rel, resolved, dynamic, typeOnly, line: lineOf(sourceFile, specifier.getStart()) });
  }
  function visit(node) {
    if (ts.isImportDeclaration(node)) {
      const specifier = node.moduleSpecifier;
      if (ts.isStringLiteral(specifier)) record(specifier, false, node.importClause?.isTypeOnly ?? false, imports);
    } else if (ts.isExportDeclaration(node) && node.moduleSpecifier && ts.isStringLiteral(node.moduleSpecifier)) {
      record(node.moduleSpecifier, false, node.isTypeOnly, reexports);
    } else if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
      const argument = node.arguments[0];
      if (argument && ts.isStringLiteral(argument)) record(argument, true, false, imports);
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return { imports, reexports };
}

function collectRefs(sourceFile, checker) {
  const httpRefs = [];
  const storageRefs = [];
  function isDeclarationName(node) {
    const parent = node.parent;
    if (!parent) return false;
    if (ts.isImportSpecifier(parent)) return parent.name === node || parent.propertyName === node;
    if (ts.isImportClause(parent)) return parent.name === node;
    if (ts.isNamespaceImport(parent)) return parent.name === node;
    if (ts.isVariableDeclaration(parent)) return parent.name === node;
    if (ts.isParameter(parent)) return parent.name === node;
    if (ts.isFunctionDeclaration(parent)) return parent.name === node;
    if (ts.isClassDeclaration(parent)) return parent.name === node;
    if (ts.isPropertyDeclaration(parent)) return parent.name === node;
    if (ts.isPropertyAssignment(parent)) return parent.name === node;
    if (ts.isPropertyAccessExpression(parent)) return parent.name === node;
    return false;
  }
  function visit(node) {
    if (ts.isIdentifier(node) && !isDeclarationName(node)) {
      const symbol = unwrap(checker.getSymbolAtLocation(node), checker);
      if (symbol) {
        const parent = node.parent;
        const inImport = parent && (ts.isImportSpecifier(parent) || ts.isImportClause(parent) || ts.isNamespaceImport(parent));
        if (!inImport && isAngularHttp(symbol)) {
          httpRefs.push({ line: lineOf(sourceFile, node.getStart()), symbol: symbol.name });
        }
        if (!inImport && (isDomFetch(symbol) || resolvesToDomFetch(symbol, checker))) {
          httpRefs.push({ line: lineOf(sourceFile, node.getStart()), symbol: symbol.name });
        }
        if (isDomStorage(symbol)) {
          storageRefs.push({ line: lineOf(sourceFile, node.getStart()), symbol: symbol.name });
        }
      }
    }
    if (ts.isPropertyAccessExpression(node) && ts.isIdentifier(node.expression)) {
      const name = node.name.text;
      const target = node.expression.text;
      if (STORAGE_NAMES.has(name) && (target === 'window' || target === 'globalThis')) {
        const line = lineOf(sourceFile, node.name.getStart());
        if (!storageRefs.some((entry) => entry.line === line)) storageRefs.push({ line, symbol: name });
      } else if (STORAGE_NAMES.has(name)) {
        const targetSymbol = unwrap(checker.getSymbolAtLocation(node.expression), checker);
        if (!targetSymbol || symbolIsUnresolved(targetSymbol)) {
          const symbol = unwrap(checker.getSymbolAtLocation(node.name), checker);
          if (!symbol || isDomStorage(symbol)) {
            const line = lineOf(sourceFile, node.name.getStart());
            if (!storageRefs.some((entry) => entry.line === line)) storageRefs.push({ line, symbol: name });
          }
        }
      }
      if (FETCH_SYMBOLS.has(name) && (target === 'window' || target === 'globalThis')) {
        const line = lineOf(sourceFile, node.name.getStart());
        if (!httpRefs.some((entry) => entry.line === line)) httpRefs.push({ line, symbol: name });
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(sourceFile);
  return { httpRefs, storageRefs };
}

function symbolIsUnresolved(symbol) {
  if (!symbol) return true;
  const declarations = symbol.declarations;
  if (!declarations || declarations.length === 0) return true;
  return false;
}

export function resolveRoot(input) {
  if (input) return path.resolve(input);
  return path.resolve(here, '..');
}

export function resolveProject(root, input) {
  if (input) return path.resolve(input);
  return path.join(root, 'frontend', 'tsconfig.app.json');
}

export async function analyzeFrontend({ root, project } = {}) {
  const resolvedRoot = resolveRoot(root);
  const projectPath = resolveProject(resolvedRoot, project);
  let configText;
  try {
    configText = await readFile(projectPath, 'utf8');
  } catch {
    throw new Error(`cannot read project ${projectPath}`);
  }
  const parsed = ts.parseConfigFileTextToJson(projectPath, configText);
  if (parsed.error) throw new Error(`cannot parse project ${projectPath}`);
  const base = path.dirname(projectPath);
  const config = ts.parseJsonConfigFileContent(parsed.config, ts.sys, base);
  if (config.errors.length > 0) {
    throw new Error(`cannot parse project ${projectPath}: ${config.errors.map((entry) => ts.flattenDiagnosticMessageText(entry.messageText, ' ')).join('; ')}`);
  }
  if (config.fileNames.length === 0) throw new Error(`project ${projectPath} includes no source files`);
  const options = { ...config.options };
  const program = ts.createProgram(config.fileNames, options);
  const checker = program.getTypeChecker();
  const diagnostics = [];
  const seen = new Set();
  function report(rule, file, line, message) {
    const key = `${rule} ${file}:${line}`;
    if (seen.has(key)) return;
    seen.add(key);
    diagnostics.push({ rule, path: file, line, message });
  }
  const graph = new Map();
  for (const fileName of program.getRootFileNames()) {
    const sourceFile = program.getSourceFile(fileName);
    if (!sourceFile || sourceFile.isDeclarationFile) continue;
    const rel = toPosix(path.relative(resolvedRoot, fileName));
    let edges;
    try {
      edges = collectEdges(sourceFile, rel, options, resolvedRoot);
    } catch (error) {
      throw new Error(`cannot resolve imports in ${rel}: ${error.message}`);
    }
    const refs = collectRefs(sourceFile, checker);
    graph.set(rel, { path: rel, kind: classifyFile(rel, sourceFile), ...edges, ...refs, ...collectNaming(sourceFile), exportedTypes: exportedTypes(sourceFile) });
  }
  for (const [rel, node] of graph) {
    for (const edge of [...node.imports, ...node.reexports]) {
      const target = edge.resolved;
      if (node.kind === 'core' && target.startsWith('frontend/src/app/features/')) {
        report('FRONT001', rel, edge.line, `core must not depend on feature ${target}`);
      }
      if (target === LEGACY_FORMS_MODULE) {
        report('FRONT006', rel, edge.line, 'forms must use Signal Forms from @angular/forms/signals, not Reactive or template-driven forms from @angular/forms');
      }
      const fromFeature = featureOf(rel);
      const toFeature = target.startsWith('external:') ? null : featureOf(target);
      if (toFeature && fromFeature !== toFeature && !isFeatureIndex(target, toFeature)) {
        const from = fromFeature ? `feature ${fromFeature}` : rel;
        report('FRONT001', rel, edge.line, `${from} must import feature ${toFeature} through features/${toFeature}/index.ts, not ${target}`);
      }
    }
    if (isLooseFeatureFile(rel)) {
      report('FRONT011', rel, 1, `only index.ts and ${featureOf(rel)}.routes.ts live at the feature root; move ${path.posix.basename(rel)} into a folder named for its purpose`);
    }
    if (TYPE_SUFFIX_FILE.test(rel)) {
      report('FRONT004', rel, 1, `file name must describe its purpose without a type suffix: ${path.posix.basename(rel)}`);
    }
    for (const entry of node.suffixedClasses) {
      report('FRONT004', rel, entry.line, `class ${entry.name} must be named for its purpose without a type suffix`);
    }
    for (const entry of node.initializerTokens) {
      report('FRONT007', rel, entry.line, `use ${INITIALIZER_TOKENS.get(entry.token)}() instead of the ${entry.token} token`);
    }
    for (const entry of node.exportedTypes.slice(1)) {
      report('FRONT010', rel, entry.line, `${entry.name} must live in its own file; a type used only here stays in this file without export`);
    }
    for (const entry of node.inlineViews) {
      report('FRONT008', rel, entry.line, `component must ${INLINE_VIEW_PROPERTIES.get(entry.property)}`);
    }
    for (const entry of node.styleFiles) {
      report('FRONT009', rel, entry.line, `component must not declare ${entry.property}; style the template with Tailwind classes`);
    }
    for (const entry of node.rootInjectables) {
      report('FRONT005', rel, entry.line, `${entry.name} must use @Service() instead of @Injectable({ providedIn: 'root' })`);
    }
    if (node.kind === 'component') {
      const httpLines = new Set();
      for (const ref of node.httpRefs) {
        if (!httpLines.has(ref.line)) {
          httpLines.add(ref.line);
          report('FRONT002', rel, ref.line, `component must not use ${ref.symbol} directly; use the feature adapter`);
        }
      }
      for (const ref of node.storageRefs) {
        report('FRONT003', rel, ref.line, `component must not use ${ref.symbol} directly`);
      }
    }
  }
  diagnostics.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : a.line - b.line || (a.rule < b.rule ? -1 : 1)));
  return { diagnostics, files: [...graph.values()] };
}

function formatText(diagnostics) {
  return diagnostics.map((entry) => `${entry.rule} ${entry.path}:${entry.line} ${entry.message}`).join('\n');
}

function parseArgs(argv) {
  const args = { root: undefined, project: undefined, format: 'text' };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (token === '--root') {
      args.root = argv[index + 1];
      index += 1;
    } else if (token === '--project') {
      args.project = argv[index + 1];
      index += 1;
    } else if (token === '--format') {
      args.format = argv[index + 1];
      index += 1;
    } else if (token === '--help' || token === '-h') {
      args.help = true;
    } else {
      throw new Error(`unknown argument ${token}`);
    }
  }
  if (args.format !== 'text' && args.format !== 'json') throw new Error(`unknown format ${args.format}`);
  return args;
}

async function main(argv) {
  let args;
  try {
    args = parseArgs(argv);
  } catch (error) {
    console.error(`check-frontend: ${error.message}`);
    return 2;
  }
  if (args.help) {
    console.log('usage: node dune/check-frontend.mjs [--root <dir>] [--project <tsconfig>] [--format text|json]');
    return 0;
  }
  let result;
  try {
    result = await analyzeFrontend({ root: args.root, project: args.project });
  } catch (error) {
    console.error(`check-frontend: ${error.message}`);
    return 2;
  }
  if (args.format === 'json') {
    console.log(JSON.stringify(result.diagnostics, null, 2));
  } else if (result.diagnostics.length > 0) {
    console.log(formatText(result.diagnostics));
  }
  if (result.diagnostics.length > 0) {
    console.log(`check-frontend: FAIL (${result.diagnostics.length} diagnostics)`);
    return 1;
  }
  console.log('check-frontend: OK');
  return 0;
}

const invoked = process.argv[1] ? toPosix(path.resolve(process.argv[1])) === toPosix(fileURLToPath(import.meta.url)) : false;
if (invoked) {
  const code = await main(process.argv.slice(2));
  process.exitCode = code;
}
