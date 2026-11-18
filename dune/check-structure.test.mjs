import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(scriptDir, 'check-structure.mjs');

async function makeRepo() {
  const dir = await mkdtemp(path.join(tmpdir(), 'dune-structure-'));
  async function write(rel, content = '') {
    const full = path.join(dir, rel);
    await mkdir(path.dirname(full), { recursive: true });
    await writeFile(full, content);
  }
  async function git(args) {
    await run('git', args, { cwd: dir });
  }
  return { dir, write, git, cleanup: () => rm(dir, { recursive: true, force: true }) };
}

const VALID_COMPOSE = [
  'services:',
  '  db:',
  '    image: postgres:18',
  '    ports:',
  '      - "5432:5432"',
  '  api:',
  '    build:',
  '      context: ./backend',
  '    healthcheck:',
  '      test: ["CMD", "bash", "-c", "true"]',
  '  gateway:',
  '    build:',
  '      context: ./gateway',
  '    ports:',
  '      - "${API_PORT:-8080}:8080"',
  '  web:',
  '    ports:',
  '      - "8081:8080"',
  'volumes:',
  '  pgdata:',
  '',
].join('\n');

const VALID_NGINX = 'location /api/v1/ {\n  proxy_pass http://gateway:8080;\n}\n';

const VALID_GATEWAY_PROJECT = '<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup>'
  + '<PackageReference Include="Yarp.ReverseProxy" Version="2.3.0" />'
  + '</ItemGroup></Project>';

const MONOLITH_EXCLUDES = '<PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);Tests/**;Platform/**;Hosts/**;Infrastructure/**;Shared/**;Features/Tasks/**</DefaultItemExcludes></PropertyGroup>';

const PLATFORM_REFERENCE = '<ItemGroup><ProjectReference Include="Platform/TemplateApp.Platform.csproj" /></ItemGroup>';

function monolithProject(extra = '') {
  return `<Project Sdk="Microsoft.NET.Sdk.Web">${MONOLITH_EXCLUDES}${PLATFORM_REFERENCE}`
    + `<ItemGroup><Compile Remove="Features/**/*Tests.cs" /><Compile Remove="Tests/**/*.cs" />${extra}</ItemGroup></Project>`;
}

const PLATFORM_PROJECT = '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><OutputType>Library</OutputType>'
  + '<EnableDefaultItems>false</EnableDefaultItems>'
  + "<BackendRoot>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..'))</BackendRoot></PropertyGroup>"
  + '<ItemGroup><Compile Include="$(BackendRoot)/Infrastructure/**/*.cs" /><Compile Include="$(BackendRoot)/Shared/**/*.cs" /></ItemGroup></Project>';

const TASKS_HOST_COMPILE = [
  '<Compile Include="*.cs" />',
  '<Compile Include="$(BackendRoot)/Features/Tasks/**/*.cs" />',
  '<Compile Include="$(BackendRoot)/Features/Health/**/*.cs" />',
  '<Compile Remove="$(BackendRoot)/Features/**/*Tests.cs" />',
];

function tasksHostProject({ compile = TASKS_HOST_COMPILE, references = ['../../Platform/TemplateApp.Platform.csproj'] } = {}) {
  return '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
    + "<BackendRoot>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)../..'))</BackendRoot></PropertyGroup>"
    + `<ItemGroup>${compile.join('')}</ItemGroup>`
    + `<ItemGroup>${references.map((reference) => `<ProjectReference Include="${reference}" />`).join('')}</ItemGroup></Project>`;
}

async function seedValid(repo) {
  for (const feature of ['Auth', 'Users', 'Tasks', 'Health']) {
    await repo.write(`backend/Features/${feature}/${feature}Feature.cs`, ' ');
    await repo.write(`backend/Features/${feature}/Ping${feature}/Ping${feature}Endpoint.cs`, ' ');
    await repo.write(`backend/Features/${feature}/Ping${feature}/Ping${feature}Tests.cs`, ' ');
  }
  await repo.write('backend/Program.cs', ' ');
  await repo.write('backend/TemplateApp.csproj', monolithProject());
  await repo.write('backend/Platform/TemplateApp.Platform.csproj', PLATFORM_PROJECT);
  await repo.write('backend/Hosts/Tasks/TemplateApp.Tasks.csproj', tasksHostProject());
  await repo.write('backend/Hosts/Tasks/Program.cs', ' ');
  await repo.write(
    'backend/Tests/TemplateApp.Tests.csproj',
    '<Project><ItemGroup><ProjectReference Include="../TemplateApp.csproj" />'
      + '<Compile Include="..\\Features\\**\\*Tests.cs" />'
      + '</ItemGroup></Project>',
  );
  await repo.write('compose.yaml', VALID_COMPOSE);
  await repo.write('frontend/nginx.conf', VALID_NGINX);
  await repo.write('gateway/TemplateApp.Gateway.csproj', VALID_GATEWAY_PROJECT);
  await repo.git(['init']);
  await repo.git(['add', '.']);
}

async function runGate(dir) {
  try {
    const { stdout } = await run(process.execPath, [script], { cwd: dir });
    return { code: 0, output: stdout };
  } catch (error) {
    return { code: error.code ?? 1, output: `${error.stdout ?? ''}${error.stderr ?? ''}` };
  }
}

describe('check-structure', () => {
  it('acepta un repo colocado y sin residuos', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza una slice sin su test', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await rm(path.join(repo.dir, 'backend/Features/Auth/PingAuth/PingAuthTests.cs'));
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /slice Auth\/PingAuth sin PingAuthTests\.cs/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza la carpeta Tests/ dentro de una feature', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/Tests/TasksTests.cs', ' ');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /tests de Tasks fuera de sus slices/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza producto que compila tests', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/TemplateApp.csproj', '<Project></Project>');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /no excluye tests del producto/);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta el monolito y el host de Tasks que solo referencian Platform', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
        tasksHostProject({
          compile: [
            '<Compile Include="*.cs" />',
            String.raw`<Compile Include="..\..\Features\Tasks\**\*.cs" />`,
            String.raw`<Compile Remove="..\..\Features\**\*Tests.cs" />`,
          ],
          references: [String.raw`..\..\Platform\TemplateApp.Platform.csproj`],
        }),
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  for (const [name, rel, content, expected] of [
    [
      'el monolito que referencia el host de Tasks',
      'backend/TemplateApp.csproj',
      monolithProject('<ProjectReference Include="Hosts/Tasks/TemplateApp.Tasks.csproj" />'),
      /límite de despliegue: backend\/TemplateApp\.csproj referencia backend\/Hosts\/Tasks\/TemplateApp\.Tasks\.csproj/,
    ],
    [
      'el host de Tasks que referencia el monolito',
      'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
      tasksHostProject({ references: ['../../Platform/TemplateApp.Platform.csproj', '../../TemplateApp.csproj'] }),
      /límite de despliegue: backend\/Hosts\/Tasks\/TemplateApp\.Tasks\.csproj referencia backend\/TemplateApp\.csproj/,
    ],
    [
      'el host de Tasks que compila Features/Users',
      'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
      tasksHostProject({ compile: [...TASKS_HOST_COMPILE, '<Compile Include="../../Features/Users/**/*.cs" />'] }),
      /límite de despliegue: backend\/Hosts\/Tasks\/TemplateApp\.Tasks\.csproj compila \.\.\/\.\.\/Features\/Users\/\*\*\/\*\.cs/,
    ],
    [
      'el host de Tasks que compila Infrastructure en vez de referenciar Platform',
      'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
      tasksHostProject({ compile: [...TASKS_HOST_COMPILE, '<Compile Include="$(BackendRoot)/Infrastructure/**/*.cs" />'] }),
      /límite de despliegue: backend\/Hosts\/Tasks\/TemplateApp\.Tasks\.csproj compila \$\(BackendRoot\)\/Infrastructure/,
    ],
    [
      'el monolito que vuelve a incluir Features/Tasks con un Compile Include',
      'backend/TemplateApp.csproj',
      monolithProject('<Compile Include="Features/Tasks/**/*.cs" Exclude="Features/Tasks/**/*Tests.cs" />'),
      /límite de despliegue: backend\/TemplateApp\.csproj compila Features\/Tasks\/\*\*\/\*\.cs/,
    ],
    [
      'el monolito sin excluir Features/Tasks',
      'backend/TemplateApp.csproj',
      `<Project>${PLATFORM_REFERENCE}<ItemGroup><Compile Remove="Features/**/*Tests.cs" /><Compile Remove="Tests/**/*.cs" /></ItemGroup></Project>`,
      /límite de despliegue: backend\/TemplateApp\.csproj no excluye Features\/Tasks\/\*\*/,
    ],
    [
      'el host de Tasks que no quita los tests',
      'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
      tasksHostProject({ compile: TASKS_HOST_COMPILE.slice(0, 3) }),
      /TemplateApp\.Tasks\.csproj no excluye tests del producto: falta Compile Remove "Features\/\*\*\/\*Tests\.cs"/,
    ],
    [
      'un bus en el host de Tasks',
      'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
      tasksHostProject().replace('</Project>', '<ItemGroup><PackageReference Include="MediatR" Version="12.0.0" /></ItemGroup></Project>'),
      /bus o mediator antes del paso 25: backend\/Hosts\/Tasks\/TemplateApp\.Tasks\.csproj \(MediatR\)/,
    ],
    [
      'DisableRateLimiting en el Program.cs del host de Tasks',
      'backend/Hosts/Tasks/Program.cs',
      'app.MapGet("/", () => "ok").DisableRateLimiting();\n',
      /DisableRateLimiting solo en .*: backend\/Hosts\/Tasks\/Program\.cs/,
    ],
  ]) {
    it(`rechaza ${name}`, async () => {
      const repo = await makeRepo();
      try {
        await seedValid(repo);
        await repo.write(rel, content);
        const result = await runGate(repo.dir);
        assert.equal(result.code, 1, result.output);
        assert.match(result.output, expected);
      } finally {
        await repo.cleanup();
      }
    });
  }

  it('rechaza un repo sin el host de Tasks', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await rm(path.join(repo.dir, 'backend/Hosts'), { recursive: true, force: true });
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /límite de despliegue: falta backend\/Hosts\/Tasks\/TemplateApp\.Tasks\.csproj/);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta paquetes y usings que no son bus', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/TemplateApp.csproj',
        monolithProject(
          '<PackageReference Include="FluentValidation" Version="12.0.0" />'
            + '<PackageReference Include="MediatorKit" Version="1.0.0" />',
        ),
      );
      await repo.write('backend/Features/Tasks/TaskModule.cs', 'using ErrorOr;\nusing TemplateApp.Shared.Messaging;\n');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta InternalsVisibleTo para TemplateApp.Tests', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/TemplateApp.csproj',
        monolithProject('<InternalsVisibleTo Include="TemplateApp.Tests" />'),
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  for (const [name, rel, content] of [
    ['InternalsVisibleTo a otro ensamblado en el csproj', 'backend/TemplateApp.csproj', monolithProject('<InternalsVisibleTo Include="TemplateApp.Tools" />')],
    ['InternalsVisibleTo como AssemblyAttribute en Directory.Build.props', 'Directory.Build.props', '<Project><ItemGroup><AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo"><_Parameter1>DynamicProxyGenAssembly2</_Parameter1></AssemblyAttribute></ItemGroup></Project>'],
    ['atributo InternalsVisibleTo en el código', 'backend/Infrastructure/AssemblyInfo.cs', '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TemplateApp.Benchmarks")]\n'],
    ['atributo InternalsVisibleTo en Properties/', 'backend/Properties/AssemblyInfo.cs', '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TemplateApp.Benchmarks")]\n'],
    ['InternalsVisibleTo en un props importado por el backend', 'backend/Build/Internals.props', '<Project><ItemGroup><InternalsVisibleTo Include="TemplateApp.Tools" /></ItemGroup></Project>'],
    ['InternalsVisibleTo con otro atributo antes de Include', 'backend/TemplateApp.csproj', monolithProject('<InternalsVisibleTo Condition="true" Include="TemplateApp.Tools" />')],
    ['InternalsVisibleTo con comillas simples', 'backend/TemplateApp.csproj', monolithProject('<InternalsVisibleTo Include=\'TemplateApp.Tools\' />')],
    ['AssemblyAttribute con _Parameter1 como atributo', 'backend/TemplateApp.csproj', monolithProject('<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo" _Parameter1="TemplateApp.Tools" />')],
    ['atributo InternalsVisibleTo con argumento con nombre', 'backend/Infrastructure/AssemblyInfo.cs', '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(assemblyName: "TemplateApp.Benchmarks")]\n'],
    ['atributo InternalsVisibleTo con una constante', 'backend/Infrastructure/AssemblyInfo.cs', '[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(Names.Benchmarks)]\n'],
  ]) {
    it(`rechaza ${name}`, async () => {
      const repo = await makeRepo();
      try {
        await seedValid(repo);
        await repo.write(rel, content);
        const result = await runGate(repo.dir);
        assert.equal(result.code, 1);
        assert.match(result.output, /InternalsVisibleTo solo para TemplateApp\.Tests/);
      } finally {
        await repo.cleanup();
      }
    });
  }

  for (const [name, rel, content] of [
    ['paquete MediatR en el producto', 'backend/TemplateApp.csproj', monolithProject('<PackageReference Include="MediatR" Version="12.0.0" />')],
    ['paquete MassTransit en Directory.Build.props', 'Directory.Build.props', '<Project><ItemGroup><PackageReference Include="MassTransit.RabbitMQ" Version="8.0.0" /></ItemGroup></Project>'],
    ['paquete Wolverine en Dune.props', 'dune/Dune.props', '<Project><ItemGroup><PackageReference Include="WolverineFx" Version="3.0.0" /></ItemGroup></Project>'],
    ['global using de NServiceBus en el csproj', 'backend/TemplateApp.csproj', monolithProject('<Using Include="NServiceBus" />')],
    ['using de Wolverine en el código', 'backend/Infrastructure/Composition.cs', 'using Wolverine;\n'],
    ['global using de Rebus en el código', 'backend/Shared/GlobalUsings.cs', 'global using Rebus.Bus;\n'],
    ['alias de MediatR en el código', 'backend/Features/Tasks/TaskModule.cs', 'using Bus = MediatR.IMediator;\n'],
  ]) {
    it(`rechaza ${name}`, async () => {
      const repo = await makeRepo();
      try {
        await seedValid(repo);
        await repo.write(rel, content);
        const result = await runGate(repo.dir);
        assert.equal(result.code, 1);
        assert.match(result.output, /bus o mediator antes del paso 25/);
      } finally {
        await repo.cleanup();
      }
    });
  }

  it('acepta un memo por request y nombres que solo se parecen a caché', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/Features/Auth/Access/AccessPolicy.cs',
        'using Microsoft.AspNetCore.Identity;\n'
          + 'public sealed class AccessPolicy { private readonly Dictionary<string, AccessGrant> _grants = new(); }\n',
      );
      await repo.write('backend/Shared/Cacheable.cs', 'namespace TemplateApp.Shared.Caching;\npublic interface ICacheable { }\n');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  for (const [name, rel, content] of [
    ['paquete de HybridCache en el producto', 'backend/TemplateApp.csproj', monolithProject('<PackageReference Include="Microsoft.Extensions.Caching.Hybrid" Version="10.0.0" />')],
    ['paquete de FusionCache en Directory.Build.props', 'Directory.Build.props', '<Project><ItemGroup><PackageReference Include="ZiggyCreatures.FusionCache" Version="2.0.0" /></ItemGroup></Project>'],
    ['using de IMemoryCache en AccessPolicy', 'backend/Features/Auth/Access/AccessPolicy.cs', 'using Microsoft.Extensions.Caching.Memory;\npublic sealed class AccessPolicy { }\n'],
    ['IMemoryCache con nombre completo', 'backend/Features/Auth/Access/AccessPolicy.cs', 'public sealed class AccessPolicy(Microsoft.Extensions.Caching.Memory.IMemoryCache cache) { }\n'],
    ['registro de AddMemoryCache', 'backend/Features/Auth/AuthFeature.cs', 'services.AddMemoryCache();\n'],
    ['registro de AddHybridCache', 'backend/Program.cs', 'builder.Services.AddHybridCache();\n'],
    ['caché de salida en un endpoint', 'backend/Features/Tasks/ListTasks/ListTasksEndpoint.cs', 'tasks.MapGet("/", Handle).CacheOutput();\n'],
  ]) {
    it(`rechaza ${name}`, async () => {
      const repo = await makeRepo();
      try {
        await seedValid(repo);
        await repo.write(rel, content);
        const result = await runGate(repo.dir);
        assert.equal(result.code, 1);
        assert.match(result.output, /caché entre requests antes del paso 33/);
      } finally {
        await repo.cleanup();
      }
    });
  }

  it('acepta la exención del limiter en Health', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/Features/Health/HealthFeature.cs',
        'RouteGroupBuilder health = api.MapGroup("/health").AllowAnonymous().DisableRateLimiting();\n',
      );
      await repo.write(
        'backend/Features/Health/PingHealth/PingHealthEndpoint.cs',
        '[DisableRateLimiting]\npublic static class PingHealthEndpoint { }\n',
      );
      await repo.write(
        'backend/Infrastructure/Http/ApiRateLimiting.cs',
        'limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(Partition);\n',
      );
      await repo.write(
        'backend/Infrastructure/Http/InternalApi.cs',
        'app.MapGroup(Prefix).DisableRateLimiting().ExcludeFromDescription();\n',
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  for (const [name, rel, content] of [
    ['DisableRateLimiting en otro archivo de Infrastructure/Http', 'backend/Infrastructure/Http/InternalApiExtras.cs', 'app.MapGroup("/internal/v2").DisableRateLimiting();\n'],
    ['DisableRateLimiting en un endpoint de Tasks', 'backend/Features/Tasks/ListTasks/ListTasksEndpoint.cs', 'tasks.MapGet("/", Handle).DisableRateLimiting();\n'],
    ['el atributo [DisableRateLimiting] en Users', 'backend/Features/Users/GetUser/GetUserEndpoint.cs', '[DisableRateLimiting]\npublic static class GetUserEndpoint { }\n'],
    ['DisableRateLimitingAttribute en Infrastructure', 'backend/Infrastructure/Http/VersionedApi.cs', 'group.WithMetadata(new DisableRateLimitingAttribute());\n'],
    ['DisableRateLimiting en Program.cs', 'backend/Program.cs', 'app.MapGroup("/api").DisableRateLimiting();\n'],
  ]) {
    it(`rechaza ${name}`, async () => {
      const repo = await makeRepo();
      try {
        await seedValid(repo);
        await repo.write(rel, content);
        const result = await runGate(repo.dir);
        assert.equal(result.code, 1);
        assert.match(result.output, /limiter global: DisableRateLimiting solo en backend\/Features\/Health y backend\/Infrastructure\/Http\/InternalApi\.cs/);
      } finally {
        await repo.cleanup();
      }
    });
  }

  it('acepta la API con una lista de puertos vacía', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('compose.yaml', VALID_COMPOSE.replace('      context: ./backend\n', '      context: ./backend\n    ports: []\n'));
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  for (const [name, rel, content, message] of [
    [
      'la API con puerto publicado',
      'compose.yaml',
      VALID_COMPOSE.replace('      context: ./backend\n', '      context: ./backend\n    ports:\n      - "8080:8080"\n'),
      /el servicio api de compose\.yaml publica puertos/,
    ],
    [
      'un servicio nuevo de la API con puerto publicado',
      'compose.yaml',
      VALID_COMPOSE.replace('  web:\n', '  tasks:\n    ports:\n      - "8082:8080"\n  web:\n'),
      /el servicio tasks de compose\.yaml publica puertos/,
    ],
    [
      'la API con la clave "ports" entre comillas',
      'compose.yaml',
      VALID_COMPOSE.replace('      context: ./backend\n', '      context: ./backend\n    "ports":\n      - "8080:8080"\n'),
      /el servicio api de compose\.yaml publica puertos/,
    ],
    [
      'la API con puertos en forma de lista en línea',
      'compose.yaml',
      VALID_COMPOSE.replace('      context: ./backend\n', '      context: ./backend\n    ports: ["8080:8080"]\n'),
      /el servicio api de compose\.yaml publica puertos/,
    ],
    [
      'el gateway con una lista de puertos vacía',
      'compose.yaml',
      VALID_COMPOSE.replace('    ports:\n      - "${API_PORT:-8080}:8080"\n', '    ports: []\n'),
      /necesita el servicio gateway con el puerto publicado/,
    ],
    [
      'el gateway sin puerto publicado',
      'compose.yaml',
      VALID_COMPOSE.replace('    ports:\n      - "${API_PORT:-8080}:8080"\n', ''),
      /necesita el servicio gateway con el puerto publicado/,
    ],
    [
      'nginx que reenvía directo a la API',
      'frontend/nginx.conf',
      'location /api/v1/ {\n  proxy_pass http://api:8080;\n}\n',
      /frontend\/nginx\.conf reenvía a http:\/\/api:8080/,
    ],
    [
      'un gateway que referencia el backend',
      'gateway/TemplateApp.Gateway.csproj',
      '<Project><ItemGroup><ProjectReference Include="../backend/TemplateApp.csproj" /></ItemGroup></Project>',
      /el gateway reparte requests y no compila código de features/,
    ],
  ]) {
    it(`rechaza ${name}`, async () => {
      const repo = await makeRepo();
      try {
        await seedValid(repo);
        await repo.write(rel, content);
        const result = await runGate(repo.dir);
        assert.equal(result.code, 1, result.output);
        assert.match(result.output, /gateway como única entrada/);
        assert.match(result.output, message);
      } finally {
        await repo.cleanup();
      }
    });
  }

  it('acepta severidades bajo secciones que cubren C#', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        '.editorconfig',
        'root = true\n\n[*.cs]\ndotnet_diagnostic.DUNE0001.severity = error\n\n'
          + '[*.{cs,csproj}]\ndotnet_diagnostic.CA1062.severity = none\n',
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza severidades bajo una sección que no cubre C#', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        '.editorconfig',
        'root = true\n\n[*.md]\ntrim_trailing_whitespace = false\n\ndotnet_diagnostic.DUNE0001.severity = error\n',
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /severidades que no aplican a \*\.cs: línea 6 bajo \[\*\.md\]/);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta cache Angular ignorada', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('.gitignore', '*.db\n*.db-shm\n*.db-wal\n');
      await repo.write('frontend/.angular/cache/22.2.1/frontend/angular-compiler.db', 'cache');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza db ignorada con git add forzado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('.gitignore', '*.db\n*.db-shm\n*.db-wal\n');
      await repo.git(['add', '.gitignore']);
      await repo.write('frontend/.angular/cache/22.2.1/frontend/angular-compiler.db', 'cache');
      await repo.git(['add', '-f', 'frontend/.angular/cache/22.2.1/frontend/angular-compiler.db']);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /angular-compiler\.db/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza git anidado como directorio', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/vendor/lib/.git/HEAD', 'ref: refs/heads/main');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /git anidado/);
      assert.match(result.output, /vendor\/lib\/\.git/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza git anidado como archivo', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/vendor/.git', 'gitdir: ../../elsewhere');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /git anidado/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza bin versionado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/bin/Debug/output.dll', 'binary');
      await repo.git(['add', '-f', 'backend/Features/Tasks/bin/Debug/output.dll']);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /bin\/Debug\/output\.dll/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza artefacto borrado pero indexado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/bundle.nupkg', 'binary');
      await repo.git(['add', 'backend/Features/Tasks/bundle.nupkg']);
      await rm(path.join(repo.dir, 'backend/Features/Tasks/bundle.nupkg'), { force: true });
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /bundle\.nupkg/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza artefacto rastreado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/bundle.nupkg', 'binary');
      await repo.git(['add', 'backend/Features/Tasks/bundle.nupkg']);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /bundle\.nupkg/);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta una feature nueva con slices y sus tests', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Billing/BillingFeature.cs', ' ');
      await repo.write('backend/Features/Billing/Contracts/InvoiceResponse.cs', ' ');
      await repo.write('backend/Features/Billing/ChargeCard/ChargeCardEndpoint.cs', ' ');
      await repo.write('backend/Features/Billing/ChargeCard/ChargeCardTests.cs', ' ');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza una feature nueva sin slices', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Billing/BillingModule.cs', ' ');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /Billing sin slices/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza inventario de tests sin glob de Features', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/Tests/TemplateApp.Tests.csproj',
        '<Project><ItemGroup></ItemGroup></Project>',
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /no referencia el producto/);
    } finally {
      await repo.cleanup();
    }
  });
});
