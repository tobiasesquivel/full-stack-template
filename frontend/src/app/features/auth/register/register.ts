import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, email, form, maxLength, minLength, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { Session } from '../session';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-register',
  imports: [FormField, FormRoot, RouterLink],
  template: `
    <section aria-labelledby="register-title">
      <h1 id="register-title">Crear cuenta</h1>
      <form [formRoot]="registerForm">
        <div>
          <label for="register-user">Usuario</label>
          <input id="register-user" type="text" [formField]="registerForm.userName" autocomplete="username" />
          @if (registerForm.userName().touched() && registerForm.userName().invalid()) {
            <p class="field-error">El usuario es obligatorio (máximo 50 caracteres).</p>
          }
        </div>
        <div>
          <label for="register-email">Correo</label>
          <input id="register-email" type="email" [formField]="registerForm.email" autocomplete="email" />
          @if (registerForm.email().touched() && registerForm.email().invalid()) {
            <p class="field-error">Escribe un correo válido.</p>
          }
        </div>
        <div>
          <label for="register-display">Nombre visible</label>
          <input id="register-display" type="text" [formField]="registerForm.displayName" autocomplete="nickname" />
        </div>
        <div>
          <label for="register-password">Contraseña</label>
          <input
            id="register-password"
            type="password"
            [formField]="registerForm.password"
            autocomplete="new-password"
          />
          @if (registerForm.password().touched() && registerForm.password().invalid()) {
            <p class="field-error">La contraseña necesita entre 6 y 128 caracteres.</p>
          }
        </div>
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <button type="submit" [disabled]="registerForm().submitting()">
          {{ registerForm().submitting() ? 'Creando…' : 'Crear cuenta' }}
        </button>
      </form>
      <p>¿Ya tienes cuenta? <a routerLink="/login">Entra</a>.</p>
    </section>
  `,
})
export class Register {
  private readonly session = inject(Session);

  private readonly account = signal({ userName: '', email: '', displayName: '', password: '' });
  protected readonly registerForm = form(
    this.account,
    (path) => {
      required(path.userName);
      maxLength(path.userName, 50);
      required(path.email);
      email(path.email);
      maxLength(path.email, 256);
      required(path.password);
      minLength(path.password, 6);
      maxLength(path.password, 128);
    },
    { submission: { action: () => this.createAccount() } },
  );
  protected readonly error = signal<string | null>(null);

  private async createAccount(): Promise<undefined> {
    this.error.set(null);
    try {
      const { userName, email, displayName, password } = this.account();
      await this.session.register({
        userName,
        email,
        password,
        displayName: displayName.trim() ? displayName : userName,
      });
    } catch (err) {
      this.error.set(describeHttpError(err));
    }
    return undefined;
  }
}
