import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Session } from '../session';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section aria-labelledby="login-title">
      <h1 id="login-title">Entrar</h1>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div>
          <label for="login-user">Usuario</label>
          <input id="login-user" type="text" formControlName="userName" autocomplete="username" required />
          @if (form.controls.userName.touched && form.controls.userName.invalid) {
            <p class="field-error">Escribe tu nombre de usuario.</p>
          }
        </div>
        <div>
          <label for="login-password">Contraseña</label>
          <input
            id="login-password"
            type="password"
            formControlName="password"
            autocomplete="current-password"
            required
          />
          @if (form.controls.password.touched && form.controls.password.invalid) {
            <p class="field-error">Escribe tu contraseña.</p>
          }
        </div>
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <button type="submit" [disabled]="loading()">
          {{ loading() ? 'Entrando…' : 'Entrar' }}
        </button>
      </form>
      <p>¿Sin cuenta? <a routerLink="/register">Crea una cuenta</a>.</p>
    </section>
  `,
})
export class Login {
  private readonly session = inject(Session);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.group({
    userName: ['', [Validators.required, Validators.maxLength(50)]],
    password: ['', Validators.required],
  });
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    try {
      const { userName, password } = this.form.getRawValue();
      await this.session.login(userName ?? '', password ?? '');
    } catch (err) {
      this.error.set(
        err instanceof HttpErrorResponse && err.status === 401
          ? 'Usuario o contraseña incorrectos.'
          : describeHttpError(err),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
