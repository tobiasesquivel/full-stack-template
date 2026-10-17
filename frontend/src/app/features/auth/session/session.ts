import { Service, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { UsersApi, type UserResponse } from '../../users';
import { AuthApi } from '../api/auth-api';
import type { AuthResponse } from '../api/auth-response';
import type { OpenSessionRequest } from '../api/open-session-request';
import type { RegisterRequest } from '../api/register-request';
import { loginUrl } from './return-url';

type SessionState =
  | { status: 'restoring' }
  | { status: 'open'; user: UserResponse }
  | { status: 'closed' };

@Service()
export class Session {
  private readonly authApi = inject(AuthApi);
  private readonly usersApi = inject(UsersApi);
  private readonly router = inject(Router);

  private readonly state = signal<SessionState>({ status: 'restoring' });
  private token: string | null = null;
  private refreshing: Promise<void> | null = null;

  readonly status = computed(() => this.state().status);
  readonly currentUser = computed(() => {
    const state = this.state();
    return state.status === 'open' ? state.user : null;
  });
  readonly isOpen = computed(() => this.state().status === 'open');

  accessToken(): string | null {
    return this.token;
  }

  async restore(): Promise<void> {
    try {
      await this.authApi.issueCsrf();
      await this.refresh();
      const user = await this.usersApi.me();
      this.state.set({ status: 'open', user });
    } catch {
      this.clear();
    }
  }

  async register(request: RegisterRequest): Promise<void> {
    await this.authApi.issueCsrf();
    this.apply(await this.authApi.register(request));
  }

  async open(request: OpenSessionRequest): Promise<void> {
    await this.authApi.issueCsrf();
    this.apply(await this.authApi.open(request));
  }

  async close(): Promise<void> {
    await this.authApi.issueCsrf();
    await this.authApi.close();
    this.clear();
  }

  refresh(): Promise<void> {
    this.refreshing ??= this.rotate().finally(() => {
      this.refreshing = null;
    });
    return this.refreshing;
  }

  end(): void {
    this.clear();
    void this.router.navigateByUrl(loginUrl(this.router, this.router.url));
  }

  replaceUser(user: UserResponse): void {
    if (this.state().status === 'open') {
      this.state.set({ status: 'open', user });
    }
  }

  private async rotate(): Promise<void> {
    try {
      const response = await this.authApi.refresh();
      this.token = response.accessToken;
    } catch (error) {
      this.clear();
      throw error;
    }
  }

  private clear(): void {
    this.token = null;
    this.state.set({ status: 'closed' });
  }

  private apply(response: AuthResponse): void {
    this.token = response.accessToken;
    this.state.set({ status: 'open', user: response.user });
  }
}
