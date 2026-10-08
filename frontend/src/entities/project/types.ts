import type { AccessType } from "@/shared/config/access-type";
import type { IssueSubmissionMode } from "@/entities/issue";

export interface ProjectItemDto {
  id: string;
  issueId: string;
  sortKey: string;
  isOptional: boolean;
  maxScore: number | null;
  title: string | null;
  status: string | null;
  accessType: AccessType | null;
  submissionMode: IssueSubmissionMode;
  selfCheckInstructions: string | null;
}

export interface ProjectDetailDto {
  id: string;
  authorId: string;
  title: string;
  description: string;
  detailedDescription: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
  isAutoReviewEnabled: boolean;
  items: ProjectItemDto[];
}

export interface UpdateProjectRequest {
  title: string;
  description: string;
  detailedDescription?: string;
}

export interface CreateProjectIssueRequest {
  title: string;
  content: string;
  submissionMode: IssueSubmissionMode;
  selfCheckInstructions?: string | null;
}

export interface MoveProjectIssueRequest {
  afterSortKey?: string;
  beforeSortKey?: string;
}

export interface ProjectReviewContextDto {
  id: string;
  projectId: string;
  guidelinesMarkdown: string;
  isAutoReviewEnabled: boolean;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
  updatedAt: string;
}

export interface UpdateProjectReviewContextRequest {
  guidelinesMarkdown: string;
  isAutoReviewEnabled: boolean;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
}
