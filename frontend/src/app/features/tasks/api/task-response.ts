export interface TaskResponse {
  id: string;
  title: string;
  isCompleted: boolean;
  createdAt: string;
  updatedAt: string | null;
  version: number;
  projectId: string | null;
}
