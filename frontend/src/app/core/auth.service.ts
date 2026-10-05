import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { AuthResponse, RegisterPayload, TokenResponse, UserResponse } from './api-models';

/**
 * Sesión del usuario. El access token vive solo en memoria (nunca en
 * localStorage ni sessionStorage); la sesión persistente es la cookie
 * HttpOnly de refresh que el navegador gestiona en el mismo origen.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly currentUser = signal<UserResponse | null>(null);
  readonly initialized = signal(false);

  private accessToken: string | null = null;
  private refreshPromise: Promise<TokenResponse> | null = null;
  private readyResolve: () => void = () => undefined;
  private readonly readyPromise = new Promise<void>((resolve) => {
    this.readyResolve = resolve;
  });

  /** Se resuelve cuando el arranque (restaurar sesión) termina. */
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
    this.currentUser.set(user);
  }

  /** Arranque no bloqueante: pide CSRF e intenta restaurar la sesión. */
  bootstrap(): void {
    this.ensureCsrf()
      .then(() => this.refreshNow())
      .then(() => this.loadMe())
      .catch(() => this.clearSession())
      .finally(() => {
        this.initialized.set(true);
        this.readyResolve();
      });
  }

  async register(payload: RegisterPayload): Promise<void> {
    await this.ensureCsrf();
    const response = await firstValueFrom(this.http.post<AuthResponse>('/api/v1/auth/register', payload));
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

  /**
   * Cierra la sesión en el servidor (revoca la cookie) y limpia la memoria.
   * Si falla (p. ej. sin red), la sesión local se conserva y el error
   * lo muestra quien llama con una acción de reintento explícita.
   */
  async logout(): Promise<void> {
    await this.ensureCsrf();
    await firstValueFrom(this.http.post('/api/v1/auth/logout', {}, { responseType: 'text' }));
    this.clearSession();
    await this.router.navigate(['/login']);
  }

  /** Renovación con singleflight: las llamadas concurrentes comparten una sola petición. */
  refreshNow(): Promise<TokenResponse> {
    if (!this.refreshPromise) {
      this.refreshPromise = this.doRefresh();
    }
    return this.refreshPromise;
  }

  clearSession(): void {
    this.accessToken = null;
    this.currentUser.set(null);
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
    this.currentUser.set(response.user);
  }

  private async loadMe(): Promise<void> {
    const me = await firstValueFrom(this.http.get<UserResponse>('/api/v1/users/me'));
    this.currentUser.set(me);
  }

  /** Descarga la cookie antiforgery; exigible antes de las mutaciones de auth. */
  async ensureCsrf(): Promise<void> {
    await firstValueFrom(this.http.get('/api/v1/auth/csrf', { responseType: 'text' }));
  }
}
