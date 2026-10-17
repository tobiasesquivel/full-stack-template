import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required } from '@angular/forms/signals';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Session } from '../session';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-login',
  imports: [FormField, FormRoot, RouterLink],
  template: `
    <section aria-labelledby="login-title">
      <h1 id="login-title">Entrar</h1>
      <form [formRoot]="loginForm">
        <div>
          <label for="login-user">Usuario</label>
          <input id="login-user" type="text" [formField]="loginForm.userName" autocomplete="username" />
          @if (loginForm.userName().touched() && loginForm.userName().invalid()) {
            <p class="field-error">Escribe tu nombre de usuario.</p>
          }
        </div>
        <div>
          <label for="login-password">Contraseña</label>
          <input
            id="login-password"
            type="password"
            [formField]="loginForm.password"
            autocomplete="current-password"
          />
          @if (loginForm.password().touched() && loginForm.password().invalid()) {
            <p class="field-error">Escribe tu contraseña.</p>
          }
        </div>
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <button type="submit" [disabled]="loginForm().submitting()">
          {{ loginForm().submitting() ? 'Entrando…' : 'Entrar' }}
        </button>
      </form>
      <p>¿Sin cuenta? <a routerLink="/register">Crea una cuenta</a>.</p>
    </section>
  `,
})
export class Login {
  private readonly session = inject(Session);

  private readonly credentials = signal({ userName: '', password: '' });
  protected readonly loginForm = form(
    this.credentials,
    (path) => {
      required(path.userName);
      maxLength(path.userName, 50);
      required(path.password);
    },
    { submission: { action: () => this.enter() } },
  );
  protected readonly error = signal<string | null>(null);

  private async enter(): Promise<undefined> {
    this.error.set(null);
    try {
      const { userName, password } = this.credentials();
      await this.session.login(userName, password);
    } catch (err) {
      this.error.set(
        err instanceof HttpErrorResponse && err.status === 401
          ? 'Usuario o contraseña incorrectos.'
          : describeHttpError(err),
      );
    }
    return undefined;
  }
}
