// Contratos HTTP consumidos del backend (/api/v1, JSON en camelCase).
// Fuente: docs/adr/0003-full-stack-template-dune.md y contrato de integración.

export interface UserResponse {
  authId: string;
  userName: string;
  email: string;
  displayName: string;
  timezone: string;
  roles: string[];
  permissions: string[];
}

export interface AuthResponse {
  user: UserResponse;
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface TokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export interface RegisterPayload {
  userName: string;
  email: string;
  password: string;
  displayName: string;
}

export interface UpdateProfilePayload {
  displayName: string;
  timezone: string;
}
