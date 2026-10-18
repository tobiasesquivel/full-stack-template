import { Service, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { UsersApi, type UserResponse } from '../../users';
import { AuthApi } from '../api/auth-api';
import type { OpenSessionRequest } from '../api/open-session-request';
import type { RegisterRequest } from '../api/register-request';
import { loginUrl, returnTarget } from './return-url';

type SessionState =
  { status: 'restoring' } | { status: 'open'; user: UserResponse } | { status: 'closed' };

type SessionMessage = 'opened' | 'closed';

const GUEST_PATHS = new Set(['/login', '/register']);

@Service()
export class Session {
  private readonly authApi = inject(AuthApi);
  private readonly usersApi = inject(UsersApi);
  private readonly router = inject(Router);

  private readonly state = signal<SessionState>({ status: 'restoring' });
  private readonly channel = new BroadcastChannel('session');

  readonly status = computed(() => this.state().status);
  readonly currentUser = computed(() => {
    const state = this.state();
    return state.status === 'open' ? state.user : null;
  });
  readonly isOpen = computed(() => this.state().status === 'open');

  constructor() {
    this.channel.onmessage = (event: MessageEvent<SessionMessage>) => {
      void this.follow(event.data);
    };
  }

  async restore(): Promise<void> {
    try {
      await this.authApi.issueCsrf();
      const user = await this.usersApi.me();
      this.state.set({ status: 'open', user });
    } catch {
      this.clear();
    }
  }

  async register(request: RegisterRequest): Promise<void> {
    await this.authApi.issueCsrf();
    this.state.set({ status: 'open', user: await this.authApi.register(request) });
    this.announce('opened');
  }

  async open(request: OpenSessionRequest): Promise<void> {
    await this.authApi.issueCsrf();
    this.state.set({ status: 'open', user: await this.authApi.open(request) });
    this.announce('opened');
  }

  async close(): Promise<void> {
    await this.authApi.issueCsrf();
    await this.authApi.close();
    this.clear();
    this.announce('closed');
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

  private async follow(message: SessionMessage): Promise<void> {
    if (message === 'closed') {
      if (this.state().status === 'open') {
        this.clear();
        await this.router.navigateByUrl('/login');
      }
      return;
    }
    await this.restore();
    const url = this.router.url;
    if (this.isOpen() && GUEST_PATHS.has(url.split(/[?#]/)[0])) {
      await this.router.navigateByUrl(
        returnTarget(this.router.parseUrl(url).queryParamMap.get('returnUrl')),
      );
    }
  }

  private announce(message: SessionMessage): void {
    this.channel.postMessage(message);
  }

  private clear(): void {
    this.state.set({ status: 'closed' });
  }
}
