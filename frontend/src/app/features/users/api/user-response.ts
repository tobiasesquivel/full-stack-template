import type { LastClosure } from './last-closure';

export interface UserResponse {
  authId: string;
  userName: string;
  email: string;
  displayName: string;
  timezone: string;
  roles: string[];
  permissions: string[];
  accountStatus: 'active' | 'scheduled' | 'closing';
  closureId: string | null;
  closesAt: string | null;
  lastClosure: LastClosure | null;
}
