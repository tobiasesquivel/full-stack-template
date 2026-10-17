import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, required } from '@angular/forms/signals';
import { Session } from '../../auth';
import { UsersApi } from '../users-api';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-profile',
  imports: [FormField, FormRoot],
  template: `
    <section aria-labelledby="profile-title">
      <h1 id="profile-title">Mi perfil</h1>

      @if (loading()) {
        <p role="status">Cargando perfil…</p>
      } @else if (error()) {
        <p role="alert">{{ error() }}</p>
        <button type="button" (click)="load()">Reintentar</button>
      } @else {
        <dl>
          <dt>Usuario</dt>
          <dd>{{ session.currentUser()?.userName }}</dd>
          <dt>Correo</dt>
          <dd>{{ session.currentUser()?.email }}</dd>
        </dl>

        <form [formRoot]="profileForm">
          <div>
            <label for="profile-display">Nombre visible</label>
            <input id="profile-display" type="text" [formField]="profileForm.displayName" />
            @if (profileForm.displayName().touched() && profileForm.displayName().invalid()) {
              <p class="field-error">El nombre visible es obligatorio.</p>
            }
          </div>
          <div>
            <label for="profile-timezone">Zona horaria (IANA)</label>
            <input
              id="profile-timezone"
              type="text"
              [formField]="profileForm.timezone"
              placeholder="UTC"
            />
            @if (profileForm.timezone().touched() && profileForm.timezone().invalid()) {
              <p class="field-error">La zona horaria es obligatoria, por ejemplo UTC o Europe/Madrid.</p>
            }
          </div>
          @if (saveError()) {
            <p role="alert">{{ saveError() }}</p>
          }
          @if (saved()) {
            <p role="status">Perfil actualizado.</p>
          }
          <button type="submit" [disabled]="profileForm().submitting()">
            {{ profileForm().submitting() ? 'Guardando…' : 'Guardar' }}
          </button>
        </form>
      }
    </section>
  `,
})
export class Profile {
  protected readonly session = inject(Session);
  private readonly usersApi = inject(UsersApi);

  private readonly profile = signal({ displayName: '', timezone: '' });
  protected readonly profileForm = form(
    this.profile,
    (path) => {
      required(path.displayName);
      required(path.timezone);
    },
    { submission: { action: () => this.save() } },
  );
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly saved = signal(false);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const me = await this.usersApi.me();
      this.session.setUser(me);
      this.profile.set({ displayName: me.displayName, timezone: me.timezone });
    } catch (err) {
      this.error.set(describeHttpError(err));
    } finally {
      this.loading.set(false);
    }
  }

  private async save(): Promise<undefined> {
    this.saveError.set(null);
    this.saved.set(false);
    try {
      const { displayName, timezone } = this.profile();
      const updated = await this.usersApi.updateMe({
        displayName: displayName.trim(),
        timezone: timezone.trim(),
      });
      this.session.setUser(updated);
      this.saved.set(true);
    } catch (err) {
      this.saveError.set(describeHttpError(err));
    }
    return undefined;
  }
}
