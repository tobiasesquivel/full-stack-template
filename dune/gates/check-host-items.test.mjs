import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { hostFromDeclaration, referenceViolations, violations } from './check-host-items.mjs';

const monolith = hostFromDeclaration('backend/Hosts/Monolith/TemplateApp.csproj', { DuneHostExcludedFeatures: 'Tasks' });
const tasks = hostFromDeclaration('backend/Hosts/Tasks/TemplateApp.Tasks.csproj', { DuneHostFeatures: 'Tasks,Health' });

const MONOLITH_FILES = [
  'backend/Hosts/Monolith/Program.cs',
  'backend/Hosts/Monolith/MonolithHost.cs',
  'backend/Hosts/Monolith/Internal/Generated/WolverineHandlers/GeneratedHandlerRegistry.cs',
  'backend/Features/Auth/AuthFeature.cs',
  'backend/Features/Users/UsersFeature.cs',
  'backend/Features/Projects/ProjectsFeature.cs',
  'backend/Features/Health/HealthFeature.cs',
];

const TASKS_FILES = [
  'backend/Hosts/Tasks/Program.cs',
  'backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/GeneratedHandlerRegistry.cs',
  'backend/Features/Tasks/TasksFeature.cs',
  'backend/Features/Health/HealthFeature.cs',
];

describe('check-host-items', () => {
  it('lee qué compila cada host de la declaración de su .csproj', () => {
    assert.equal(monolith.scope, 'el host compila sus archivos y Features salvo Features/Tasks (DuneHostExcludedFeatures de su .csproj)');
    assert.equal(tasks.scope, 'el host compila sus archivos y Features/Tasks, Features/Health (DuneHostFeatures de su .csproj)');
  });

  it('revisa un host inventado con su propia declaración, sin editar el gate', () => {
    const billing = hostFromDeclaration('backend/Hosts/Billing/Acme.Billing.csproj', { DuneHostFeatures: ' Invoices , Health ' });
    assert.deepEqual(violations(billing, ['backend/Hosts/Billing/Program.cs', 'backend/Features/Invoices/InvoicesFeature.cs', 'backend/Features/Health/HealthFeature.cs']), []);
    const [failure, ...rest] = violations(billing, ['backend/Features/Tasks/TasksFeature.cs']);
    assert.deepEqual(rest, []);
    assert.ok(failure?.startsWith('límite de despliegue: backend/Hosts/Billing/Acme.Billing.csproj compila backend/Features/Tasks/TasksFeature.cs; el host compila sus archivos y Features/Invoices, Features/Health'), failure);
  });

  it('deja entrar al monolito una feature inventada sin editar su declaración', () => {
    assert.deepEqual(violations(monolith, ['backend/Features/Invoices/InvoicesFeature.cs']), []);
  });

  for (const [name, declaration, expected] of [
    ['un host sin declaración', {}, 'no declara qué features despliega'],
    ['un host con la declaración vacía', { DuneHostFeatures: ' , ' }, 'no declara qué features despliega'],
    ['un host con las dos declaraciones', { DuneHostFeatures: 'Invoices', DuneHostExcludedFeatures: 'Tasks' }, 'declara DuneHostFeatures y DuneHostExcludedFeatures'],
  ]) {
    it(`rechaza ${name}`, () => {
      const host = hostFromDeclaration('backend/Hosts/Billing/Acme.Billing.csproj', declaration);
      assert.ok(host.problem?.startsWith(`límite de despliegue: backend/Hosts/Billing/Acme.Billing.csproj ${expected}`), host.problem);
    });
  }

  it('acepta la referencia a Platform, también con barras invertidas, y el analyzer de Dune', () => {
    assert.deepEqual(referenceViolations(monolith, ['backend/Platform/TemplateApp.Platform.csproj']), []);
    assert.deepEqual(referenceViolations(tasks, [String.raw`backend\Platform\TemplateApp.Platform.csproj`]), []);
    assert.deepEqual(referenceViolations(tasks, ['dune/Dune.Analyzers/Dune.Analyzers.csproj']), []);
  });

  for (const [name, host, reference] of [
    ['el monolito que referencia el host de Tasks', monolith, 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj'],
    ['el host de Tasks que referencia una biblioteca del repo con otro nombre', tasks, 'backend/Billing/Billing.Core.csproj'],
    ['el host de Tasks que referencia un proyecto de fuera de backend', tasks, 'dune/Dune.CommentVerifier/Dune.CommentVerifier.csproj'],
  ]) {
    it(`rechaza ${name}`, () => {
      const [failure, ...rest] = referenceViolations(host, ['backend/Platform/TemplateApp.Platform.csproj', reference]);
      assert.deepEqual(rest, []);
      assert.ok(
        failure?.startsWith(`límite de despliegue: ${host.project} referencia ${reference}; un host referencia solo backend/Platform/TemplateApp.Platform.csproj (y el analyzer de Dune`),
        failure,
      );
    });
  }

  it('acepta lo que compila cada host hoy, también con barras invertidas', () => {
    assert.deepEqual(violations(monolith, MONOLITH_FILES), []);
    assert.deepEqual(violations(tasks, TASKS_FILES), []);
    assert.deepEqual(violations(monolith, ['backend\\Features\\Auth\\AuthFeature.cs']), []);
  });

  for (const [name, host, file] of [
    ['un archivo de Tasks en el monolito', monolith, 'backend/Features/Tasks/TasksFeature.cs'],
    ['un archivo de Tasks en minúsculas en el monolito', monolith, 'backend/Features/tasks/TasksFeature.cs'],
    ['un archivo de Users en el host de Tasks', tasks, 'backend/Features/Users/UsersFeature.cs'],
    ['Infrastructure en un host', tasks, 'backend/Infrastructure/Persistence/ContextRegistration.cs'],
    ['Shared en un host', monolith, 'backend/Shared/Domain/IAuditable.cs'],
    ['los tests del backend en un host', monolith, 'backend/Tests/Startup/ConcurrentStartupTests.cs'],
    ['el test de una slice en un host', monolith, 'backend/Features/Auth/OpenSession/OpenSessionTests.cs'],
    ['un test en la carpeta del host', monolith, 'backend/Hosts/Monolith/ProbeTests.CS'],
    ['un archivo del otro host', monolith, 'backend/Hosts/Tasks/Program.cs'],
    ['una carpeta hermana con otras mayúsculas', tasks, 'backend/Hosts/tasks/Leak.cs'],
    ['una feature con otras mayúsculas en el host de Tasks', tasks, 'backend/Features/tasks/TasksFeature.cs'],
    ['un archivo fuera del repo', tasks, '../outside/Leak.cs'],
  ]) {
    it(`rechaza ${name}`, () => {
      const found = violations(host, [file]);
      assert.equal(found.length, 1, found.join('\n'));
      assert.match(found[0], new RegExp(`límite de despliegue: ${host.project.replaceAll('.', '\\.')} compila `));
    });
  }
});
