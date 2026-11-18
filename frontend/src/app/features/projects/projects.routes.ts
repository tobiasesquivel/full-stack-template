import { Routes } from '@angular/router';

export const projectsRoutes: Routes = [
  {
    path: 'projects',
    loadComponent: () => import('./projects/projects').then((m) => m.Projects),
  },
];
