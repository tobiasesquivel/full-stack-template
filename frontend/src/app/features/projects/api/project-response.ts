export interface ProjectResponse {
  id: string;
  name: string;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string | null;
  version: number;
  ownerName: string;
  taskCount: number;
  completedTaskCount: number;
}
