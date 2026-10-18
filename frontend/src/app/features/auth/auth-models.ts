import type { UserResponse } from '../users';

export interface AuthResponse {
  user: UserResponse;
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface RegisterRequest {
  userName: string;
  email: string;
  password: string;
  displayName: string;
}
