import type { ProjectResponse } from './project-response';

export interface ProjectListResponse {
  items: ProjectResponse[];
  nextCursor: string | null;
}
