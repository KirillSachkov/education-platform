import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  CreateProjectIssueRequest,
  MoveProjectIssueRequest,
  ProjectDetailDto,
  ProjectReviewContextDto,
  UpdateProjectRequest,
  UpdateProjectReviewContextRequest,
} from "./types";

export const projectsApi = {
  getProjectDetail: async (projectId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<ProjectDetailDto>>(
      `/projects/${projectId}/detail`,
      { signal },
    );
    return res.data;
  },

  updateProject: async ({ projectId, request }: { projectId: string; request: UpdateProjectRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/projects/${projectId}`,
      request,
    );
    return res.data;
  },

  createIssue: async ({ projectId, request }: { projectId: string; request: CreateProjectIssueRequest }) => {
    const res = await apiClient.post<Envelope<string>>(
      `/projects/${projectId}/issues/`,
      request,
    );
    return res.data;
  },

  detachIssue: async ({ projectId, issueId }: { projectId: string; issueId: string }) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/projects/${projectId}/issues/${issueId}`,
    );
    return res.data;
  },

  moveIssue: async ({ projectId, issueId, request }: { projectId: string; issueId: string; request: MoveProjectIssueRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/projects/${projectId}/issues/${issueId}/move`,
      request,
    );
    return res.data;
  },

  publishProject: async (projectId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/projects/${projectId}/publish`,
    );
    return res.data.result!;
  },

  archiveProject: async (projectId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/projects/${projectId}/archive`,
    );
    return res.data.result!;
  },

  restoreProject: async (projectId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/projects/${projectId}/restore`,
    );
    return res.data.result!;
  },

  getReviewContext: async (
    projectId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<ProjectReviewContextDto | null>>(
      `/projects/${projectId}/review-context/`,
      { signal },
    );
    return res.data;
  },

  updateReviewContext: async ({
    projectId,
    request,
  }: {
    projectId: string;
    request: UpdateProjectReviewContextRequest;
  }) => {
    const res = await apiClient.put<Envelope<string>>(
      `/projects/${projectId}/review-context`,
      request,
    );
    return res.data;
  },
};

export const projectsQueryOptions = {
  baseKey: "projects",
};

export const projectDetailQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: [projectsQueryOptions.baseKey, projectId, "detail"],
    queryFn: ({ signal }) => projectsApi.getProjectDetail(projectId, { signal }),
    select: (data) => data.result!,
    enabled: !!projectId,
  });

export const projectReviewContextQueryOptions = (projectId: string) =>
  queryOptions({
    queryKey: [projectsQueryOptions.baseKey, projectId, "review-context"],
    queryFn: ({ signal }) => projectsApi.getReviewContext(projectId, { signal }),
    select: (data) => data.result ?? null,
    enabled: !!projectId,
  });
