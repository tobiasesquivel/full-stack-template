import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { Session } from './features/auth';
import { describeHttpError } from './core/http-error';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly session = inject(Session);
  protected readonly logoutError = signal<string | null>(null);
  protected readonly loggingOut = signal(false);

  protected async logout(): Promise<void> {
    this.loggingOut.set(true);
    this.logoutError.set(null);
    try {
      await this.session.logout();
    } catch (err) {
      this.logoutError.set(`${describeHttpError(err)} Pulsa Salir de nuevo para reintentar.`);
    } finally {
      this.loggingOut.set(false);
    }
  }
}
