import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth.service';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section aria-labelledby="register-title">
      <h1 id="register-title">Crear cuenta</h1>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div>
          <label for="register-user">Usuario</label>
          <input id="register-user" type="text" formControlName="userName" autocomplete="username" required />
          @if (form.controls.userName.touched && form.controls.userName.invalid) {
            <p class="field-error">El usuario es obligatorio (máximo 50 caracteres).</p>
          }
        </div>
        <div>
          <label for="register-email">Correo</label>
          <input id="register-email" type="email" formControlName="email" autocomplete="email" required />
          @if (form.controls.email.touched && form.controls.email.invalid) {
            <p class="field-error">Escribe un correo válido.</p>
          }
        </div>
        <div>
          <label for="register-display">Nombre visible</label>
          <input id="register-display" type="text" formControlName="displayName" autocomplete="nickname" />
        </div>
        <div>
          <label for="register-password">Contraseña</label>
          <input
            id="register-password"
            type="password"
            formControlName="password"
            autocomplete="new-password"
            required
          />
          @if (form.controls.password.touched && form.controls.password.invalid) {
            <p class="field-error">La contraseña necesita entre 6 y 128 caracteres.</p>
          }
        </div>
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <button type="submit" [disabled]="loading()">
          {{ loading() ? 'Creando…' : 'Crear cuenta' }}
        </button>
      </form>
      <p>¿Ya tienes cuenta? <a routerLink="/login">Entra</a>.</p>
    </section>
  `,
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.group({
    userName: ['', [Validators.required, Validators.maxLength(50)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    displayName: [''],
    password: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(128)]],
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
      const { userName, email, displayName, password } = this.form.getRawValue();
      await this.auth.register({
        userName: userName ?? '',
        email: email ?? '',
        password: password ?? '',
        displayName: displayName?.trim() ? displayName : (userName ?? ''),
      });
    } catch (err) {
      this.error.set(describeHttpError(err));
    } finally {
      this.loading.set(false);
    }
  }
}
