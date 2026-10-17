import type { UserResponse } from '../users';

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
}
