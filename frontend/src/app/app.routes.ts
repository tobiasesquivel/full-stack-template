import { Routes } from '@angular/router';
import { authGuard, authRoutes, guestGuard } from './features/auth';
import { tasksRoutes } from './features/tasks';
import { usersRoutes } from './features/users';

export const routes: Routes = [
  { path: '', redirectTo: 'tasks', pathMatch: 'full' },
  { path: '', canActivateChild: [guestGuard], children: authRoutes },
  { path: '', canActivateChild: [authGuard], children: [...tasksRoutes, ...usersRoutes] },
  { path: '**', redirectTo: 'tasks' },
];
