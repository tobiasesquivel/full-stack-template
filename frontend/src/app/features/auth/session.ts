import { Service, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { UsersApi, type UserResponse } from '../users';
import type { AuthResponse, RegisterRequest, TokenResponse } from './auth-models';

@Service()
export class Session {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly usersApi = inject(UsersApi);

  private readonly currentUserSignal = signal<UserResponse | null>(null);
  private readonly initializedSignal = signal(false);
  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly initialized = this.initializedSignal.asReadonly();

  private accessToken: string | null = null;
  private refreshPromise: Promise<TokenResponse> | null = null;
  private readyResolve: () => void = () => undefined;
  private readonly readyPromise = new Promise<void>((resolve) => {
    this.readyResolve = resolve;
  });

  ready(): Promise<void> {
    return this.readyPromise;
  }

  token(): string | null {
    return this.accessToken;
  }

  isAuthenticated(): boolean {
    return this.currentUser() !== null;
  }

  setUser(user: UserResponse): void {
    this.currentUserSignal.set(user);
  }

  bootstrap(): void {
    this.ensureCsrf()
      .then(() => this.refreshNow())
      .then(() => this.loadMe())
      .catch(() => this.clearSession())
      .finally(() => {
        this.initializedSignal.set(true);
        this.readyResolve();
      });
  }

  async register(request: RegisterRequest): Promise<void> {
    await this.ensureCsrf();
    const response = await firstValueFrom(this.http.post<AuthResponse>('/api/v1/auth/register', request));
    this.applyAuth(response);
    await this.router.navigate(['/tasks']);
  }

  async login(userName: string, password: string): Promise<void> {
    await this.ensureCsrf();
    const response = await firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/login', { userName, password }),
    );
    this.applyAuth(response);
    await this.router.navigate(['/tasks']);
  }

  async logout(): Promise<void> {
    await this.ensureCsrf();
    await firstValueFrom(this.http.post('/api/v1/auth/logout', {}, { responseType: 'text' }));
    this.clearSession();
    await this.router.navigate(['/login']);
  }

  refreshNow(): Promise<TokenResponse> {
    if (!this.refreshPromise) {
      this.refreshPromise = this.doRefresh();
    }
    return this.refreshPromise;
  }

  clearSession(): void {
    this.accessToken = null;
    this.currentUserSignal.set(null);
  }

  private async doRefresh(): Promise<TokenResponse> {
    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>('/api/v1/auth/refresh', {}),
      );
      this.accessToken = response.accessToken;
      return response;
    } catch (error) {
      this.clearSession();
      throw error;
    } finally {
      this.refreshPromise = null;
    }
  }

  private applyAuth(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this.currentUserSignal.set(response.user);
  }

  private async loadMe(): Promise<void> {
    const me = await this.usersApi.me();
    this.currentUserSignal.set(me);
  }

  async ensureCsrf(): Promise<void> {
    await firstValueFrom(this.http.get('/api/v1/auth/csrf', { responseType: 'text' }));
  }
}
