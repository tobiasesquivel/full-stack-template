import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { UpdateProfileRequest, UserResponse } from './users-models';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);

  me(): Promise<UserResponse> {
    return firstValueFrom(this.http.get<UserResponse>('/api/v1/users/me'));
  }

  updateMe(request: UpdateProfileRequest): Promise<UserResponse> {
    return firstValueFrom(this.http.put<UserResponse>('/api/v1/users/me', request));
  }
}
