export interface UserResponse {
  authId: string;
  userName: string;
  email: string;
  displayName: string;
  timezone: string;
  roles: string[];
  permissions: string[];
}
