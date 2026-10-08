import type { AccessType } from "@/shared/config/access-type";

export type IssueSubmissionMode = "PULL_REQUEST" | "SELF_CHECK";

export interface IssueInternalMaterialDto {
  itemType: string;
  referenceId: string;
  isRequired: boolean;
  title: string | null;
  imageUrl: string | null;
}

export interface IssueExternalLinkDto {
  url: string;
  title: string;
  isRequired: boolean;
}

export interface IssueDetailDto {
  id: string;
  projectId: string;
  title: string;
  content: string | null;
  status: string;
  accessType: AccessType;
  isAccessible: boolean;
  submissionMode: IssueSubmissionMode;
  selfCheckInstructions: string | null;
  requiresGithubConnection: boolean;
  requiresReviewApp: boolean;
  isAutoReviewEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  internalMaterials: IssueInternalMaterialDto[];
  externalLinks: IssueExternalLinkDto[];
}

export interface UpdateIssueRequest {
  title: string;
  content: string;
  accessType: AccessType;
  submissionMode: IssueSubmissionMode;
  selfCheckInstructions?: string | null;
}

export interface InternalMaterialItem {
  itemType: string;
  referenceId: string;
  isRequired: boolean;
}

export interface UpdateIssueInternalMaterialsRequest {
  items: InternalMaterialItem[];
}

export interface ExternalLinkItem {
  url: string;
  title: string;
  isRequired: boolean;
}

export interface UpdateIssueExternalLinksRequest {
  items: ExternalLinkItem[];
}

export interface ReviewSpecDto {
  id: string;
  issueId: string;
  authorPrompt: string | null;
  reviewAspects: string | null;
  isAutoReviewEnabled: boolean;
  updatedAt: string;
}

export interface UpdateReviewSpecRequest {
  authorPrompt: string | null;
  reviewAspects: string | null;
  isAutoReviewEnabled: boolean;
}
