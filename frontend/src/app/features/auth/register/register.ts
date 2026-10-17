import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, email, form, maxLength, minLength, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { Session } from '../session/session';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-register',
  imports: [FormField, FormRoot, RouterLink],
  templateUrl: './register.html',
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
