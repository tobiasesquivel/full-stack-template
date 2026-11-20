import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { UpdateProfileRequest } from './update-profile-request';
import type { UserResponse } from './user-response';

@Service()
export class UsersApi {
  private readonly http = inject(HttpClient);

  me(): Promise<UserResponse> {
    return firstValueFrom(this.http.get<UserResponse>('/api/v1/users/me'));
  }

  updateMe(request: UpdateProfileRequest): Promise<UserResponse> {
    return firstValueFrom(this.http.put<UserResponse>('/api/v1/users/me', request));
  }

  cancelClosure(closureId: string, idempotencyKey: string): Promise<void> {
    return firstValueFrom(
      this.http.post(`/api/v1/users/closures/${closureId}/cancel`, null, {
        headers: { 'Idempotency-Key': idempotencyKey },
        responseType: 'text',
      }),
    ).then(() => undefined);
  }
}
