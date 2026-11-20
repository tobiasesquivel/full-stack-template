import type { LastClosure } from './last-closure';

export interface UserResponse {
  authId: string;
  userName: string;
  email: string;
  displayName: string;
  timezone: string;
  roles: string[];
  permissions: string[];
  accountStatus: 'active' | 'closing';
  lastClosure: LastClosure | null;
}
