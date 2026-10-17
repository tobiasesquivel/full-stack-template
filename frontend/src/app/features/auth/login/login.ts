import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required } from '@angular/forms/signals';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { Session } from '../session/session';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-login',
  imports: [FormField, FormRoot, RouterLink],
  templateUrl: './login.html',
})
export class Login {
  private readonly session = inject(Session);
  private readonly router = inject(Router);

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
      await this.session.open({ userName, password });
      await this.router.navigate(['/tasks']);
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
