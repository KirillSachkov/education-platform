import { apiClient, unwrapEnvelope, type Envelope } from "@/shared/api";
import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import type {
  IssueDetailDto,
  ReviewSpecDto,
  UpdateIssueExternalLinksRequest,
  UpdateIssueInternalMaterialsRequest,
  UpdateIssueRequest,
  UpdateReviewSpecRequest,
} from "./types";

export const issuesApi = {
  getIssueDetail: async (
    issueId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<IssueDetailDto>>(
      `/issues/${issueId}/detail`,
      { signal },
    );
    return res.data;
  },

  updateIssue: async ({ issueId, request }: { issueId: string; request: UpdateIssueRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/issues/${issueId}`,
      request,
    );
    return res.data;
  },

  updateInternalMaterials: async ({
    issueId,
    request,
  }: {
    issueId: string;
    request: UpdateIssueInternalMaterialsRequest;
  }) => {
    const res = await apiClient.put<Envelope<string>>(
      `/issues/${issueId}/internal-materials`,
      request,
    );
    return res.data;
  },

  updateExternalLinks: async ({
    issueId,
    request,
  }: {
    issueId: string;
    request: UpdateIssueExternalLinksRequest;
  }) => {
    const res = await apiClient.put<Envelope<string>>(
      `/issues/${issueId}/external-links`,
      request,
    );
    return res.data;
  },

  publishIssue: async (
    issueId: string,
    options?: { notifySubscribers?: boolean },
  ): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/issues/${issueId}/publish`,
      { notifySubscribers: options?.notifySubscribers ?? true },
    );
    return res.data.result!;
  },

  archiveIssue: async (issueId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/issues/${issueId}/archive`,
    );
    return res.data.result!;
  },

  restoreIssue: async (issueId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/issues/${issueId}/restore`,
    );
    return res.data.result!;
  },

  getReviewSpec: async (
    issueId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<ReviewSpecDto | null>>(
      `/issues/${issueId}/review-spec/`,
      { signal },
    );
    return res.data;
  },

  updateReviewSpec: async ({
    issueId,
    request,
  }: {
    issueId: string;
    request: UpdateReviewSpecRequest;
  }) => {
    const res = await apiClient.put<Envelope<string>>(
      `/issues/${issueId}/review-spec`,
      request,
    );
    return res.data;
  },
};

export const issuesQueryOptions = {
  baseKey: "issues",
};

export const issueDetailQueryOptions = (issueId: string) =>
  queryOptions({
    queryKey: [issuesQueryOptions.baseKey, issueId, "detail"],
    queryFn: ({ signal }) => issuesApi.getIssueDetail(issueId, { signal }),
    select: unwrapEnvelope,
    // Старая задача остаётся на экране пока грузится новая — навигация в
    // sidebar выглядит без spinner-flash.
    placeholderData: keepPreviousData,
    enabled: !!issueId,
  });

export const reviewSpecQueryOptions = (issueId: string) =>
  queryOptions({
    queryKey: [issuesQueryOptions.baseKey, issueId, "review-spec"],
    queryFn: ({ signal }) => issuesApi.getReviewSpec(issueId, { signal }),
    select: (data) => data.result ?? null,
    enabled: !!issueId,
  });
