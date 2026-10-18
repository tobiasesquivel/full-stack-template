import { Service, inject } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { AuthResponse } from './auth-response';
import type { OpenSessionRequest } from './open-session-request';
import type { RegisterRequest } from './register-request';
import { SKIP_SESSION } from './skip-session';
import type { TokenResponse } from './token-response';

function withoutSession(): HttpContext {
  return new HttpContext().set(SKIP_SESSION, true);
}

@Service()
export class AuthApi {
  private readonly http = inject(HttpClient);

  async issueCsrf(): Promise<void> {
    await firstValueFrom(
      this.http.get('/api/v1/auth/csrf', { responseType: 'text', context: withoutSession() }),
    );
  }

  register(request: RegisterRequest): Promise<AuthResponse> {
    return firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/register', request, { context: withoutSession() }),
    );
  }

  open(request: OpenSessionRequest): Promise<AuthResponse> {
    return firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/login', request, { context: withoutSession() }),
    );
  }

  refresh(): Promise<TokenResponse> {
    return firstValueFrom(
      this.http.post<TokenResponse>('/api/v1/auth/refresh', {}, { context: withoutSession() }),
    );
  }

  async close(): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/v1/auth/logout', {}, { responseType: 'text', context: withoutSession() }),
    );
  }
}
