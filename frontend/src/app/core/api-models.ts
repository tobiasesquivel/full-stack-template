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

export interface TaskResponse {
  id: string;
  title: string;
  isCompleted: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
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

export interface UpdateTaskPayload {
  title: string;
  isCompleted: boolean;
}
