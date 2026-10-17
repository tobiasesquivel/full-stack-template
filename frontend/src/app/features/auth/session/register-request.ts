import type { UserResponse } from '../../users';

export interface RegisterRequest {
  userName: string;
  email: string;
  password: string;
  displayName: string;
}
