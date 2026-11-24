import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { HOSTS, violations } from './check-host-items.mjs';

const [monolith, tasks] = HOSTS;

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
  it('evalúa el monolito y el host de Tasks', () => {
    assert.deepEqual(
      HOSTS.map((host) => host.project),
      ['backend/Hosts/Monolith/TemplateApp.csproj', 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj'],
    );
  });

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
    ['un archivo fuera del repo', tasks, '../outside/Leak.cs'],
  ]) {
    it(`rechaza ${name}`, () => {
      const found = violations(host, [file]);
      assert.equal(found.length, 1, found.join('\n'));
      assert.match(found[0], new RegExp(`límite de despliegue: ${host.project.replaceAll('.', '\\.')} compila `));
    });
  }
});
