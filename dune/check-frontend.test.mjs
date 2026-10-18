import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { analyzeFrontend } from './check-frontend.mjs';

async function makeProject(files) {
  const root = await mkdtemp(path.join(tmpdir(), 'dune-frontend-'));
  const frontend = path.join(root, 'frontend');
  await mkdir(path.join(frontend, 'src', 'app'), { recursive: true });
  await writeFile(
    path.join(frontend, 'package.json'),
    JSON.stringify({ name: 'fixture', dependencies: {} }),
  );
  await writeFile(
    path.join(frontend, 'tsconfig.app.json'),
    JSON.stringify({ include: ['src/**/*.ts'], compilerOptions: { target: 'ES2022', module: 'preserve', skipLibCheck: true } }),
  );
  for (const [rel, content] of Object.entries(files)) {
    const full = path.join(root, rel);
    await mkdir(path.dirname(full), { recursive: true });
    await writeFile(full, content);
  }
  return { root, cleanup: () => rm(root, { recursive: true, force: true }) };
}

const ANGULAR_STUB = `declare module '@angular/core' {
  export function Component(options: unknown): unknown;
  export function Injectable(options?: unknown): unknown;
  export function inject(token: unknown): unknown;
  export function signal<T>(value: T): T;
}
declare module '@angular/common/http' {
  export class HttpClient {}
  export class HttpRequest {}
}
`;

async function seedAngular(root) {
  const types = path.join(root, 'frontend', 'node_modules', '@angular');
  await mkdir(path.join(types, 'core'), { recursive: true });
  await mkdir(path.join(types, 'common'), { recursive: true });
  await writeFile(path.join(types, 'core', 'package.json'), JSON.stringify({ name: 'core', version: '0.0.0', types: './index.d.ts' }));
  await writeFile(path.join(types, 'core', 'index.d.ts'), `export declare function Component(o: unknown): unknown;\nexport declare function Injectable(o?: unknown): unknown;\nexport declare function inject(t: unknown): unknown;\nexport declare function signal<T>(v: T): T;\n`);
  await writeFile(path.join(types, 'common', 'package.json'), JSON.stringify({ name: 'http', version: '0.0.0', types: './http.d.ts' }));
  await writeFile(path.join(types, 'common', 'http.d.ts'), `export declare class HttpClient {}\nexport declare class HttpRequest {}\n`);
  void ANGULAR_STUB;
}

describe('check-frontend', () => {
  it('accepts composition roots and other features that import through the feature index', async () => {
    const project = await makeProject({
      'frontend/src/app/app.routes.ts': `import { tasksRoutes } from './features/tasks';\nexport const routes = [...tasksRoutes];\n`,
      'frontend/src/app/features/tasks/index.ts': `export { tasksRoutes } from './tasks.routes';\n`,
      'frontend/src/app/features/tasks/tasks.routes.ts': `export const tasksRoutes = [\n  { path: 'tasks', loadComponent: () => import('./tasks/tasks').then((m) => m.Tasks) },\n];\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport type { User } from '../../users';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  readonly user: User | null = null;\n}\n`,
      'frontend/src/app/features/users/index.ts': `export type { User } from './users-models';\n`,
      'frontend/src/app/features/users/users-models.ts': `export interface User {\n  id: string;\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics, []);
    } finally {
      await project.cleanup();
    }
  });

  it('rejects composition roots and app components that reach inside a feature', async () => {
    const project = await makeProject({
      'frontend/src/app/app.routes.ts': `export const routes = [\n  { path: 'a', loadComponent: () => import('./features/tasks/tasks/tasks').then((m) => m.Tasks) },\n];\n`,
      'frontend/src/app/app.ts': `import { Component } from '@angular/core';\nimport { TasksService } from './features/tasks/tasks.service';\n@Component({ selector: 'app-root', template: '' })\nexport class App {\n  readonly service = TasksService;\n}\n`,
      'frontend/src/app/features/tasks/tasks.service.ts': `export class TasksService {}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const cross = diagnostics.filter((entry) => entry.rule === 'FRONT001').map((entry) => entry.path);
      assert.deepEqual(cross, ['frontend/src/app/app.routes.ts', 'frontend/src/app/app.ts']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts the feature HTTP adapter while rejecting component HTTP', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks.service.ts': `import { Injectable, inject } from '@angular/core';\nimport { HttpClient } from '@angular/common/http';\n@Injectable({ providedIn: 'root' })\nexport class TasksService {\n  private readonly http = inject(HttpClient);\n}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, inject } from '@angular/core';\nimport { HttpClient } from '@angular/common/http';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  private readonly http = inject(HttpClient);\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.equal(diagnostics.length, 1);
      assert.equal(diagnostics[0].rule, 'FRONT002');
      assert.match(diagnostics[0].path, /tasks\.ts$/);
    } finally {
      await project.cleanup();
    }
  });

  it('resolves relative paths and barrel reexports to the implementation target', async () => {
    const project = await makeProject({
      'frontend/src/app/features/auth/login/login.ts': `import { Component } from '@angular/core';\nimport { thing } from '../../../shared/barrel';\n@Component({ selector: 'x', template: '' })\nexport class Login {\n  readonly value = thing;\n}\n`,
      'frontend/src/app/shared/barrel.ts': `export * from './impl';\n`,
      'frontend/src/app/shared/impl.ts': `export const thing = 1;\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics, files } = await analyzeFrontend({ root: project.root });
      void diagnostics;
      const login = files.find((entry) => entry.path.endsWith('login.ts'));
      assert.ok(login);
      assert.ok(login.imports.some((edge) => edge.resolved.endsWith('shared/barrel.ts')));
      const barrel = files.find((entry) => entry.path.endsWith('barrel.ts'));
      assert.ok(barrel);
      assert.ok(barrel.reexports.some((edge) => edge.resolved.endsWith('shared/impl.ts')));
    } finally {
      await project.cleanup();
    }
  });

  it('resolves tsconfig path aliases', async () => {
    const root = await mkdtemp(path.join(tmpdir(), 'dune-frontend-'));
    const frontend = path.join(root, 'frontend');
    await mkdir(path.join(frontend, 'src', 'app'), { recursive: true });
    await writeFile(path.join(frontend, 'package.json'), JSON.stringify({ name: 'fixture', dependencies: {} }));
    await writeFile(
      path.join(frontend, 'tsconfig.app.json'),
      JSON.stringify({ include: ['src/**/*.ts'], compilerOptions: { target: 'ES2022', module: 'preserve', skipLibCheck: true, baseUrl: '.', paths: { '@shared/*': ['src/app/shared/*'] } } }),
    );
    await mkdir(path.join(root, 'frontend/src/app/features/auth/login'), { recursive: true });
    await mkdir(path.join(root, 'frontend/src/app/shared'), { recursive: true });
    await writeFile(path.join(root, 'frontend/src/app/features/auth/login/login.ts'), `import { thing } from '@shared/barrel';\nexport const value = thing;\n`);
    await writeFile(path.join(root, 'frontend/src/app/shared/barrel.ts'), `export const thing = 1;\n`);
    try {
      await seedAngular(root);
      const { files } = await analyzeFrontend({ root });
      const login = files.find((entry) => entry.path.endsWith('login.ts'));
      assert.ok(login);
      assert.ok(login.imports.some((edge) => edge.resolved.endsWith('shared/barrel.ts')));
    } finally {
      await rm(root, { recursive: true, force: true });
    }
  });

  it('rejects cross-feature imports that skip the index, models included', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { helper } from '../../users/impl';\nimport type { User } from '../../users/users-models';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  readonly value = helper;\n  readonly user: User | null = null;\n}\n`,
      'frontend/src/app/features/users/impl.ts': `export const helper = 1;\n`,
      'frontend/src/app/features/users/users-models.ts': `export interface User {\n  id: string;\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const cross = diagnostics.filter((entry) => entry.rule === 'FRONT001');
      assert.deepEqual(cross.map((entry) => entry.line), [2, 3]);
      assert.match(cross[0].path, /tasks\.ts$/);
    } finally {
      await project.cleanup();
    }
  });

  it('rejects type-only imports of foreign implementations', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport type { Helper } from '../../users/impl';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  readonly value: Helper | null = null;\n}\n`,
      'frontend/src/app/features/users/impl.ts': `export interface Helper {\n  id: string;\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.ok(diagnostics.some((entry) => entry.rule === 'FRONT001'));
    } finally {
      await project.cleanup();
    }
  });

  it('flags aliased HttpClient reexports in components', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, inject } from '@angular/core';\nimport { HttpClient as H } from '../../../http-shim';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  private readonly http = inject(H);\n}\n`,
      'frontend/src/app/http-shim.ts': `export { HttpClient } from '@angular/common/http';\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.ok(diagnostics.some((entry) => entry.rule === 'FRONT002'));
    } finally {
      await project.cleanup();
    }
  });

  it('rejects qualified storage in components while allowing local shadowing', async () => {
    const violating = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  read(): string | null {\n    return window.localStorage.getItem('k');\n  }\n}\n`,
    });
    try {
      await seedAngular(violating.root);
      const { diagnostics } = await analyzeFrontend({ root: violating.root });
      assert.ok(diagnostics.some((entry) => entry.rule === 'FRONT003'));
    } finally {
      await violating.cleanup();
    }
    const shadowed = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  read(localStorage: { getItem(k: string): string | null }): string | null {\n    return localStorage.getItem('k');\n  }\n}\n`,
    });
    try {
      await seedAngular(shadowed.root);
      const { diagnostics } = await analyzeFrontend({ root: shadowed.root });
      assert.deepEqual(diagnostics.filter((entry) => entry.rule === 'FRONT003'), []);
    } finally {
      await shadowed.cleanup();
    }
  });

  it('ignores storage names inside strings templates and regex', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nconst a = "localStorage.getItem('x')";\nconst b = \`sessionStorage \${a}\`;\nconst c = /localStorage/;\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  readonly values = [a, b, c];\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics.filter((entry) => entry.rule === 'FRONT003'), []);
    } finally {
      await project.cleanup();
    }
  });

  it('rejects fetch and XMLHttpRequest in components including globals and aliases', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  async load(): Promise<unknown> {\n    const direct = await fetch('/api/v1/tasks');\n    const global = await globalThis.fetch('/api/v1/tasks');\n    const xhr = new XMLHttpRequest();\n    return [direct, global, xhr];\n  }\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const hits = diagnostics.filter((entry) => entry.rule === 'FRONT002');
      assert.deepEqual(hits.map((entry) => [entry.line, entry.path.endsWith('tasks/tasks.ts')]), [[5, true], [6, true], [7, true]]);
    } finally {
      await project.cleanup();
    }
    const aliased = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { fetch as get } from '../../../http-ambient';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  load(): unknown {\n    return get('/api/v1/tasks');\n  }\n}\n`,
      'frontend/src/app/http-ambient.ts': `export const fetch: typeof globalThis.fetch = globalThis.fetch;\n`,
    });
    try {
      await seedAngular(aliased.root);
      const { diagnostics } = await analyzeFrontend({ root: aliased.root });
      const hits = diagnostics.filter((entry) => entry.rule === 'FRONT002');
      assert.deepEqual(hits.map((entry) => [entry.rule, entry.line]), [['FRONT002', 6]]);
    } finally {
      await aliased.cleanup();
    }
  });

  it('resolves long alias chains and terminates on import cycles', async () => {
    const chained = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { a1 } from '../../../chain';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  load(): unknown {\n    return a1('/api/v1/tasks');\n  }\n}\n`,
      'frontend/src/app/chain.ts': `import { a2 } from './chain2';\nexport const a1 = a2;\n`,
      'frontend/src/app/chain2.ts': `import { a3 } from './chain3';\nexport const a2 = a3;\n`,
      'frontend/src/app/chain3.ts': `import { a4 } from './chain4';\nexport const a3 = a4;\n`,
      'frontend/src/app/chain4.ts': `export const a4: typeof globalThis.fetch = globalThis.fetch;\n`,
    });
    try {
      await seedAngular(chained.root);
      const { diagnostics } = await analyzeFrontend({ root: chained.root });
      const hits = diagnostics.filter((entry) => entry.rule === 'FRONT002');
      assert.deepEqual(hits.map((entry) => [entry.rule, entry.line]), [['FRONT002', 6]]);
    } finally {
      await chained.cleanup();
    }
    const cyclic = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { c1 } from '../../../cycle1';\n@Component({ selector: 'x', template: '' })\nexport class Tasks {\n  load(local: string): unknown {\n    return [c1, local];\n  }\n}\n`,
      'frontend/src/app/cycle1.ts': `import { c2 } from './cycle2';\nexport const c1 = c2;\n`,
      'frontend/src/app/cycle2.ts': `import { c1 } from './cycle1';\nexport const c2 = c1;\n`,
    });
    try {
      await seedAngular(cyclic.root);
      const { diagnostics } = await analyzeFrontend({ root: cyclic.root });
      assert.deepEqual(diagnostics.filter((entry) => entry.rule === 'FRONT002'), []);
    } finally {
      await cyclic.cleanup();
    }
  });

  it('rejects core imports of features', async () => {
    const project = await makeProject({
      'frontend/src/app/core/session.ts': `import { thing } from '../features/tasks/tasks.service';\nexport const value = thing;\n`,
      'frontend/src/app/features/tasks/tasks.service.ts': `export const thing = 1;\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.ok(diagnostics.some((entry) => entry.rule === 'FRONT001'));
    } finally {
      await project.cleanup();
    }
  });

  it('fails configuration errors instead of reporting green', async () => {
    await assert.rejects(() => analyzeFrontend({ root: '/nonexistent-root-xyz', project: '/nonexistent.json' }), /cannot read project/);
  });
});
