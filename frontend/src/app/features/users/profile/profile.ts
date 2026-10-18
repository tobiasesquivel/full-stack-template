import { Component, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, required } from '@angular/forms/signals';
import { Session } from '../../auth';
import { UsersApi } from '../api/users-api';
import { describeHttpError } from '../../../core/http-error';

@Component({
  selector: 'app-profile',
  imports: [FormField, FormRoot],
  templateUrl: './profile.html',
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
      this.session.replaceUser(me);
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
      this.session.replaceUser(updated);
      this.saved.set(true);
    } catch (err) {
      this.saveError.set(describeHttpError(err));
    }
    return undefined;
  }
}
