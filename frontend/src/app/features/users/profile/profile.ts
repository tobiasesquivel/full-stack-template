import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../../core/auth.service';
import { UsersService } from '../../../core/users.service';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-profile',
  imports: [ReactiveFormsModule],
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
          <dd>{{ auth.currentUser()?.userName }}</dd>
          <dt>Correo</dt>
          <dd>{{ auth.currentUser()?.email }}</dd>
        </dl>

        <form [formGroup]="form" (ngSubmit)="save()" novalidate>
          <div>
            <label for="profile-display">Nombre visible</label>
            <input id="profile-display" type="text" formControlName="displayName" required />
            @if (form.controls.displayName.touched && form.controls.displayName.invalid) {
              <p class="field-error">El nombre visible es obligatorio.</p>
            }
          </div>
          <div>
            <label for="profile-timezone">Zona horaria (IANA)</label>
            <input
              id="profile-timezone"
              type="text"
              formControlName="timezone"
              placeholder="UTC"
              required
            />
            @if (form.controls.timezone.touched && form.controls.timezone.invalid) {
              <p class="field-error">La zona horaria es obligatoria, por ejemplo UTC o Europe/Madrid.</p>
            }
          </div>
          @if (saveError()) {
            <p role="alert">{{ saveError() }}</p>
          }
          @if (saved()) {
            <p role="status">Perfil actualizado.</p>
          }
          <button type="submit" [disabled]="saving()">
            {{ saving() ? 'Guardando…' : 'Guardar' }}
          </button>
        </form>
      }
    </section>
  `,
})
export class Profile {
  protected readonly auth = inject(AuthService);
  private readonly users = inject(UsersService);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.group({
    displayName: ['', Validators.required],
    timezone: ['', Validators.required],
  });
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly saved = signal(false);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const me = await this.users.me();
      this.auth.setUser(me);
      this.form.setValue({ displayName: me.displayName, timezone: me.timezone });
    } catch (err) {
      this.error.set(describeHttpError(err));
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.saved.set(false);
    try {
      const { displayName, timezone } = this.form.getRawValue();
      const updated = await this.users.updateMe({
        displayName: (displayName ?? '').trim(),
        timezone: (timezone ?? '').trim(),
      });
      this.auth.setUser(updated);
      this.saved.set(true);
    } catch (err) {
      this.saveError.set(describeHttpError(err));
    } finally {
      this.saving.set(false);
    }
  }
}
