import { Service, inject } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { UserResponse } from '../../users';
import type { OpenSessionRequest } from './open-session-request';
import type { RegisterRequest } from './register-request';
import { SKIP_SESSION } from './skip-session';

function sessionCall(): HttpContext {
  return new HttpContext().set(SKIP_SESSION, true);
}

@Service()
export class AuthApi {
  private readonly http = inject(HttpClient);

  async issueCsrf(): Promise<void> {
    await firstValueFrom(
      this.http.get('/api/v1/auth/csrf', { responseType: 'text', context: sessionCall() }),
    );
  }

  register(request: RegisterRequest): Promise<UserResponse> {
    return firstValueFrom(
      this.http.post<UserResponse>('/api/v1/auth/register', request, { context: sessionCall() }),
    );
  }

  open(request: OpenSessionRequest): Promise<UserResponse> {
    return firstValueFrom(
      this.http.post<UserResponse>('/api/v1/auth/login', request, { context: sessionCall() }),
    );
  }

  async close(): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/v1/auth/logout', {}, { responseType: 'text', context: sessionCall() }),
    );
  }
}
