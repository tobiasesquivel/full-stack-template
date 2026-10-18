import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { analyzeFrontend } from './check-frontend.mjs';

const ANGULAR_JSON = JSON.stringify({
  version: 1,
  projects: { frontend: { projectType: 'application', schematics: { '@schematics/angular:component': { style: 'none' } } } },
}, null, 2);

const TASKS_COMPONENT = {
  'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'app-tasks', templateUrl: './tasks.html' })\nexport class Tasks {}\n`,
};

async function makeProject(files) {
  files = { 'frontend/angular.json': ANGULAR_JSON, ...files };
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
  await writeFile(path.join(types, 'core', 'index.d.ts'), `export declare function Component(o: unknown): unknown;\nexport declare function Injectable(o?: unknown): unknown;\nexport declare function Service(o?: unknown): unknown;\nexport declare function inject<T>(token: abstract new (...args: never[]) => T): T;\nexport declare class ElementRef<T = unknown> {\n  nativeElement: T;\n}\nexport declare function input<T>(v: T): Signal<T>;\nexport interface Signal<T> {\n  (): T;\n}\nexport interface WritableSignal<T> extends Signal<T> {\n  set(v: T): void;\n  asReadonly(): Signal<T>;\n}\nexport declare function signal<T>(v: T): WritableSignal<T>;\nexport declare function computed<T>(f: () => T): Signal<T>;\nexport declare function linkedSignal<T>(f: () => T): WritableSignal<T>;\n`);
  await writeFile(path.join(types, 'common', 'package.json'), JSON.stringify({ name: 'http', version: '0.0.0', types: './http.d.ts' }));
  await writeFile(path.join(types, 'common', 'http.d.ts'), `export declare class HttpClient {}\nexport declare class HttpRequest {}\n`);
  await mkdir(path.join(types, 'router'), { recursive: true });
  await writeFile(path.join(types, 'router', 'package.json'), JSON.stringify({ name: 'router', version: '0.0.0', types: './index.d.ts' }));
  await writeFile(path.join(types, 'router', 'index.d.ts'), `export declare class Router {\n  navigate(commands: unknown[]): Promise<boolean>;\n}\nexport declare class ActivatedRoute {\n  snapshot: unknown;\n}\n`);
  await mkdir(path.join(types, 'platform-browser'), { recursive: true });
  await writeFile(path.join(types, 'platform-browser', 'package.json'), JSON.stringify({ name: 'platform-browser', version: '0.0.0', types: './index.d.ts' }));
  await writeFile(path.join(types, 'platform-browser', 'index.d.ts'), `export declare function bootstrapApplication(root: unknown, options?: unknown): Promise<unknown>;\n`);
  void ANGULAR_STUB;
}

describe('check-frontend', () => {
  it('accepts composition roots and other features that import through the feature index', async () => {
    const project = await makeProject({
      'frontend/src/app/app.routes.ts': `import { tasksRoutes } from './features/tasks';\nexport const routes = [...tasksRoutes];\n`,
      'frontend/src/app/features/tasks/index.ts': `export { tasksRoutes } from './tasks.routes';\n`,
      'frontend/src/app/features/tasks/tasks.routes.ts': `export const tasksRoutes = [\n  { path: 'tasks', loadComponent: () => import('./tasks/tasks').then((m) => m.Tasks) },\n];\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport type { User } from '../../users';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  readonly user: User | null = null;\n}\n`,
      'frontend/src/app/features/users/index.ts': `export type { User } from './api/users-models';\n`,
      'frontend/src/app/features/users/api/users-models.ts': `export interface User {\n  id: string;\n}\n`,
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
      'frontend/src/app/app.ts': `import { Component } from '@angular/core';\nimport { TasksService } from './features/tasks/tasks.service';\n@Component({ selector: 'app-root', templateUrl: './x.html' })\nexport class App {\n  readonly service = TasksService;\n}\n`,
      'frontend/src/app/features/tasks/tasks.service.ts': `export class TasksService {}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {}\n`,
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
      'frontend/src/app/features/tasks/api/tasks-api.ts': `import { Service, inject } from '@angular/core';\nimport { HttpClient } from '@angular/common/http';\n@Service()\nexport class TasksApi {\n  private readonly http = inject(HttpClient);\n}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, inject } from '@angular/core';\nimport { HttpClient } from '@angular/common/http';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  private readonly http = inject(HttpClient);\n}\n`,
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
      'frontend/src/app/features/auth/login/login.ts': `import { Component } from '@angular/core';\nimport { thing } from '../../../shared/barrel';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Login {\n  readonly value = thing;\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { helper } from '../../users/impl';\nimport type { User } from '../../users/users-models';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  readonly value = helper;\n  readonly user: User | null = null;\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport type { Helper } from '../../users/impl';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  readonly value: Helper | null = null;\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, inject } from '@angular/core';\nimport { HttpClient as H } from '../../../http-shim';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  private readonly http = inject(H);\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  read(): string | null {\n    return window.localStorage.getItem('k');\n  }\n}\n`,
    });
    try {
      await seedAngular(violating.root);
      const { diagnostics } = await analyzeFrontend({ root: violating.root });
      assert.ok(diagnostics.some((entry) => entry.rule === 'FRONT003'));
    } finally {
      await violating.cleanup();
    }
    const shadowed = await makeProject({
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  read(localStorage: { getItem(k: string): string | null }): string | null {\n    return localStorage.getItem('k');\n  }\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nconst a = "localStorage.getItem('x')";\nconst b = \`sessionStorage \${a}\`;\nconst c = /localStorage/;\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  readonly values = [a, b, c];\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  async load(): Promise<unknown> {\n    const direct = await fetch('/api/v1/tasks');\n    const global = await globalThis.fetch('/api/v1/tasks');\n    const xhr = new XMLHttpRequest();\n    return [direct, global, xhr];\n  }\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { fetch as get } from '../../../http-ambient';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  load(): unknown {\n    return get('/api/v1/tasks');\n  }\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { a1 } from '../../../chain';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  load(): unknown {\n    return a1('/api/v1/tasks');\n  }\n}\n`,
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
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { c1 } from '../../../cycle1';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  load(local: string): unknown {\n    return [c1, local];\n  }\n}\n`,
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

  it('accepts intent names without type suffixes and @Service singletons', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/api/tasks-api.ts': `import { Service } from '@angular/core';\n@Service()\nexport class TasksApi {}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {}\n`,
      'frontend/src/app/features/tasks/tasks.routes.ts': `export const tasksRoutes = [];\n`,
      'frontend/src/app/features/auth/session/auth-guard.ts': `export const authGuard = () => true;\n`,
      'frontend/src/app/features/auth/session/scoped-cache.ts': `import { Injectable } from '@angular/core';\n@Injectable()\nexport class ScopedCache {}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics, []);
    } finally {
      await project.cleanup();
    }
  });

  it('rejects type suffixes in file names and decorated class names', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks.service.ts': `import { Service } from '@angular/core';\n@Service()\nexport class TasksService {}\n`,
      'frontend/src/app/features/auth/auth.guard.ts': `export const authGuard = () => true;\n`,
      'frontend/src/app/features/tasks/tasks/tasks-view.ts': `import { Component } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class TasksComponent {}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const naming = diagnostics.filter((entry) => entry.rule === 'FRONT004').map((entry) => `${path.basename(entry.path)}:${entry.line}`);
      assert.deepEqual(naming, ['auth.guard.ts:1', 'tasks.service.ts:1', 'tasks.service.ts:3', 'tasks-view.ts:3']);
    } finally {
      await project.cleanup();
    }
  });

  it('rejects @Injectable with providedIn root in favor of @Service', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/api/tasks-api.ts': `import { Injectable } from '@angular/core';\n@Injectable({ providedIn: 'root' })\nexport class TasksApi {}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics.map((entry) => [entry.rule, entry.line]), [['FRONT005', 2]]);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts Signal Forms and rejects Reactive or template-driven forms', async () => {
    const project = await makeProject({
      'frontend/src/app/features/auth/login/login.ts': `import { Component } from '@angular/core';\nimport { FormField, form } from '@angular/forms/signals';\n@Component({ selector: 'x', templateUrl: './x.html', imports: [FormField] })\nexport class Login {\n  readonly loginForm = form;\n}\n`,
      'frontend/src/app/features/users/profile/profile.ts': `import { Component } from '@angular/core';\nimport { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';\n@Component({ selector: 'x', templateUrl: './x.html', imports: [ReactiveFormsModule] })\nexport class Profile {\n  readonly builder = FormBuilder;\n  readonly rules = Validators;\n}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';\nimport { FormsModule } from '@angular/forms';\n@Component({ selector: 'x', templateUrl: './x.html', imports: [FormsModule] })\nexport class Tasks {}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const forms = diagnostics.filter((entry) => entry.rule === 'FRONT006').map((entry) => `${path.basename(entry.path)}:${entry.line}`);
      assert.deepEqual(forms, ['tasks.ts:2', 'profile.ts:2']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts functional initializers and rejects the initializer tokens', async () => {
    const project = await makeProject({
      'frontend/src/app/app.config.ts': `import { APP_INITIALIZER as Init, inject, provideAppInitializer } from '@angular/core';\nexport const appConfig = { providers: [provideAppInitializer(() => inject(Init)), { provide: Init, multi: true, useValue: () => undefined }] };\n`,
      'frontend/src/main.ts': `import { ENVIRONMENT_INITIALIZER } from '@angular/core';\nexport const token = ENVIRONMENT_INITIALIZER;\n`,
      'frontend/src/app/features/auth/session.ts': `import { provideAppInitializer } from '@angular/core';\nexport const start = provideAppInitializer;\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const initializers = diagnostics.filter((entry) => entry.rule === 'FRONT007').map((entry) => `${path.basename(entry.path)}:${entry.line}`);
      assert.deepEqual(initializers, ['app.config.ts:1', 'main.ts:1']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts templateUrl and rejects inline template or styles', async () => {
    const project = await makeProject({
      'frontend/src/app/app.ts': `import { Component } from '@angular/core';
@Component({ selector: 'app-root', templateUrl: './app.html' })
export class App {}
`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';
@Component({
  selector: 'x',
  template: '<p></p>',
  styles: 'p {}',
})
export class Tasks {}
`,
      'frontend/src/app/features/users/profile/profile.ts': `import { Component } from '@angular/core';
@Component({ selector: 'x', templateUrl: './profile.html', styles: ['p {}'] })
export class Profile {}
`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const inline = diagnostics.filter((entry) => entry.rule === 'FRONT008').map((entry) => `${path.basename(entry.path)}:${entry.line}`);
      assert.deepEqual(inline, ['tasks.ts:4', 'tasks.ts:5', 'profile.ts:2']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts Tailwind classes in the template and rejects styleUrl or styleUrls', async () => {
    const project = await makeProject({
      'frontend/src/app/app.ts': `import { Component } from '@angular/core';
@Component({ selector: 'app-root', templateUrl: './app.html' })
export class App {}
`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component } from '@angular/core';
@Component({
  selector: 'x',
  templateUrl: './tasks.html',
  styleUrl: './tasks.css',
})
export class Tasks {}
`,
      'frontend/src/app/features/users/profile/profile.ts': `import { Component } from '@angular/core';
@Component({ selector: 'x', templateUrl: './profile.html', 'styleUrls': ['./profile.css'] })
export class Profile {}
`,
      'frontend/src/app/features/auth/login/login.ts': `import { Component } from '@angular/core';
@Component({ selector: 'x', templateUrl: './login.html', styles: 'p {}' })
export class Login {}
`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const styleFiles = diagnostics.filter((entry) => entry.rule === 'FRONT009');
      assert.deepEqual(styleFiles.map((entry) => `${path.basename(entry.path)}:${entry.line}`), ['tasks.ts:5', 'profile.ts:2']);
      assert.match(styleFiles[0].message, /styleUrl.*Tailwind/);
      assert.match(styleFiles[1].message, /styleUrls.*Tailwind/);
      const inline = diagnostics.filter((entry) => entry.rule === 'FRONT008');
      assert.deepEqual(inline.map((entry) => `${path.basename(entry.path)}:${entry.line}`), ['login.ts:2']);
      assert.match(inline[0].message, /Tailwind/);
      assert.doesNotMatch(inline[0].message, /styleUrl/);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts one exported type per file and rejects files that export several', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/index.ts': `export type { TaskResponse } from './task-response';\nexport type { TaskListResponse } from './task-list-response';\n`,
      'frontend/src/app/features/tasks/task-response.ts': `export interface TaskResponse {\n  id: string;\n}\n`,
      'frontend/src/app/features/tasks/task-list-response.ts': `import type { TaskResponse } from './task-response';\ninterface Page {\n  page: number;\n}\nexport interface TaskListResponse extends Page {\n  items: TaskResponse[];\n}\n`,
      'frontend/src/app/features/users/users-models.ts': `export interface UserResponse {\n  id: string;\n}\n\nexport type UserId = string;\nexport enum UserKind {\n  Person,\n}\n`,
      'frontend/src/app/features/auth/session.ts': `export class Session {}\nexport class SessionClock {}\nexport const timeout = 5;\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const types = diagnostics.filter((entry) => entry.rule === 'FRONT010').map((entry) => `${path.basename(entry.path)}:${entry.line}`);
      assert.deepEqual(types, ['session.ts:2', 'users-models.ts:5', 'users-models.ts:6']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts only the door and the routes at a feature root and rejects other loose files', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/index.ts': `export { tasksRoutes } from './tasks.routes';\nexport type { TaskResponse } from './api/task-response';\n`,
      'frontend/src/app/features/tasks/tasks.routes.ts': `export const tasksRoutes = [];\n`,
      'frontend/src/app/features/tasks/api/task-response.ts': `export interface TaskResponse {\n  id: string;\n}\n`,
      'frontend/src/app/features/auth/index.ts': `export { Session } from './session';\nexport { authGuard } from './auth-guard';\n`,
      'frontend/src/app/features/auth/auth.routes.ts': `export const authRoutes = [];\n`,
      'frontend/src/app/features/auth/session.ts': `export class Session {}\n`,
      'frontend/src/app/features/auth/auth-guard.ts': `export const authGuard = () => true;\n`,
      'frontend/src/app/features/auth/session/token-response.ts': `export interface TokenResponse {\n  accessToken: string;\n}\n`,
      'frontend/src/app/core/http-error.ts': `export const describe = () => '';\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const loose = diagnostics.filter((entry) => entry.rule === 'FRONT011').map((entry) => path.basename(entry.path));
      assert.deepEqual(loose, ['auth-guard.ts', 'session.ts']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts read-only state in a service and rejects a service that exposes a writable signal', async () => {
    const project = await makeProject({
      'frontend/src/app/features/auth/session/session.ts': `import { Service, computed, signal } from '@angular/core';\ntype State = { status: 'open' } | { status: 'closed' };\n@Service()\nexport class Session {\n  private readonly state = signal<State>({ status: 'closed' });\n  protected readonly draft = signal('');\n  readonly status = computed(() => this.state().status);\n  readonly user = this.state.asReadonly();\n}\n`,
      'frontend/src/app/features/tasks/api/tasks-api.ts': `import { Service, signal, linkedSignal, type WritableSignal } from '@angular/core';\ntype Writable = WritableSignal<number>;\n@Service()\nexport class TasksApi {\n  readonly page = signal(1);\n  public readonly size = linkedSignal(() => 10);\n  readonly total: Writable | null = null;\n  private readonly hidden = signal(0);\n}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, signal } from '@angular/core';\n@Component({ selector: 'x', templateUrl: './x.html' })\nexport class Tasks {\n  readonly items = signal<string[]>([]);\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      const writable = diagnostics.filter((entry) => entry.rule === 'FRONT012').map((entry) => `${path.basename(entry.path)}:${entry.line}`);
      assert.deepEqual(writable, ['tasks-api.ts:5', 'tasks-api.ts:6', 'tasks-api.ts:7']);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts routed components and the app root that inject app services, and presentation components that inject only Angular tokens', async () => {
    const project = await makeProject({
      'frontend/src/main.ts': `import { bootstrapApplication } from '@angular/platform-browser';\nimport { App } from './app/app';\nbootstrapApplication(App);\n`,
      'frontend/src/app/app.ts': `import { Component, inject } from '@angular/core';\nimport { Router } from '@angular/router';\nimport { Session } from './features/auth';\n@Component({ selector: 'app-root', templateUrl: './app.html' })\nexport class App {\n  private readonly session = inject(Session);\n  private readonly router = inject(Router);\n}\n`,
      'frontend/src/app/features/auth/index.ts': `export { Session } from './session/session';\n`,
      'frontend/src/app/features/auth/session/session.ts': `import { Service } from '@angular/core';\n@Service()\nexport class Session {}\n`,
      'frontend/src/app/features/tasks/tasks.routes.ts': `import { Profile } from './profile/profile';\nexport const tasksRoutes = [\n  { path: 'tasks', loadComponent: () => import('./tasks/tasks').then((m) => m.Tasks) },\n  { path: 'profile', component: Profile },\n  { path: 'stats', loadComponent: () => import('./stats/stats') },\n];\n`,
      'frontend/src/app/features/tasks/stats/stats.ts': `import { Component, inject } from '@angular/core';\nimport { TasksApi } from '../api/tasks-api';\n@Component({ selector: 'app-stats', templateUrl: './stats.html' })\nexport default class Stats {\n  private readonly tasksApi = inject(TasksApi);\n}\n`,
      'frontend/src/app/features/tasks/api/tasks-api.ts': `import { Service } from '@angular/core';\n@Service()\nexport class TasksApi {}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, inject } from '@angular/core';\nimport { ActivatedRoute } from '@angular/router';\nimport { TasksApi } from '../api/tasks-api';\n@Component({ selector: 'app-tasks', templateUrl: './tasks.html' })\nexport class Tasks {\n  private readonly tasksApi = inject(TasksApi);\n  private readonly route = inject(ActivatedRoute);\n}\n`,
      'frontend/src/app/features/tasks/profile/profile.ts': `import { Component } from '@angular/core';\nimport { TasksApi } from '../api/tasks-api';\n@Component({ selector: 'app-profile', templateUrl: './profile.html' })\nexport class Profile {\n  constructor(private readonly tasksApi: TasksApi) {}\n}\n`,
      'frontend/src/app/features/tasks/task-pager/task-pager.ts': `import { Component, ElementRef, inject, input } from '@angular/core';\n@Component({ selector: 'app-task-pager', templateUrl: './task-pager.html' })\nexport class TaskPager {\n  readonly page = input(1);\n  private readonly host = inject(ElementRef);\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics, []);
    } finally {
      await project.cleanup();
    }
  });

  it('rejects a presentation component that injects an app service, an alias of one, the Router or a service in its constructor', async () => {
    const project = await makeProject({
      'frontend/src/app/features/tasks/tasks.routes.ts': `export const tasksRoutes = [\n  { path: 'tasks', loadComponent: () => import('./tasks/tasks').then((m) => m.Tasks) },\n];\n`,
      'frontend/src/app/features/tasks/api/tasks-api.ts': `import { Service } from '@angular/core';\n@Service()\nexport class TasksApi {}\n`,
      'frontend/src/app/features/tasks/tasks/tasks.ts': `import { Component, inject } from '@angular/core';\nimport { TasksApi } from '../api/tasks-api';\n@Component({ selector: 'app-tasks', templateUrl: './tasks.html' })\nexport class Tasks {\n  private readonly tasksApi = inject(TasksApi);\n}\n`,
      'frontend/src/app/features/tasks/task-row/task-row.ts': `import { Component, inject } from '@angular/core';\nimport { Router } from '@angular/router';\nimport { TasksApi } from '../api/tasks-api';\nconst api = TasksApi;\n@Component({ selector: 'app-task-row', templateUrl: './task-row.html' })\nexport class TaskRow {\n  private readonly direct = inject(TasksApi);\n  private readonly aliased = inject(api);\n  private readonly router = inject(Router);\n  constructor(private readonly viaConstructor: TasksApi) {}\n}\n`,
    });
    try {
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics.map((entry) => [entry.rule, path.basename(entry.path), entry.line]), [
        ['FRONT013', 'task-row.ts', 7],
        ['FRONT013', 'task-row.ts', 8],
        ['FRONT013', 'task-row.ts', 9],
        ['FRONT013', 'task-row.ts', 10],
      ]);
      assert.equal(diagnostics[0].message, 'TaskRow is not a routed component and must not inject TasksApi; receive data with input() and report with output(), or load it from a component in tasks.routes.ts');
      assert.match(diagnostics[1].message, /must not inject TasksApi/);
      assert.match(diagnostics[2].message, /must not inject Router/);
      assert.match(diagnostics[3].message, /must not inject TasksApi/);
    } finally {
      await project.cleanup();
    }
  });

  it('accepts a CLI that generates components without a style file and rejects one that does not say so', async () => {
    const valid = await makeProject(TASKS_COMPONENT);
    try {
      await seedAngular(valid.root);
      const { diagnostics } = await analyzeFrontend({ root: valid.root });
      assert.deepEqual(diagnostics, []);
    } finally {
      await valid.cleanup();
    }
    const cases = [
      [`{
  "version": 1,
  "projects": {
    "frontend": {
      "schematics": {},
      "root": ""
    }
  }
}
`, 5, /@schematics\/angular:component/],
      [`{
  "version": 1,
  "projects": {
    "frontend": {
      "schematics": {
        "@schematics/angular:component": {
          "style": "css"
        }
      }
    }
  }
}
`, 7, /style "none"/],
      [`{
  "version": 1,
  "projects": {
    "frontend": {
      "root": ""
    }
  }
}
`, 4, /schematics/],
    ];
    for (const [config, line, message] of cases) {
      const project = await makeProject({ ...TASKS_COMPONENT, 'frontend/angular.json': config });
      try {
        await seedAngular(project.root);
        const { diagnostics } = await analyzeFrontend({ root: project.root });
        assert.deepEqual(diagnostics.map((entry) => [entry.rule, entry.path, entry.line]), [['FRONT014', 'frontend/angular.json', line]]);
        assert.match(diagnostics[0].message, message);
      } finally {
        await project.cleanup();
      }
    }
  });

  it('rejects a frontend without angular.json', async () => {
    const project = await makeProject(TASKS_COMPONENT);
    try {
      await rm(path.join(project.root, 'frontend', 'angular.json'));
      await seedAngular(project.root);
      const { diagnostics } = await analyzeFrontend({ root: project.root });
      assert.deepEqual(diagnostics.map((entry) => [entry.rule, entry.path, entry.line]), [['FRONT014', 'frontend/angular.json', 1]]);
    } finally {
      await project.cleanup();
    }
  });

  it('fails configuration errors instead of reporting green', async () => {
    await assert.rejects(() => analyzeFrontend({ root: '/nonexistent-root-xyz', project: '/nonexistent.json' }), /cannot read project/);
  });
});
