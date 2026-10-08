import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AssignmentReviewAiSettingsDto,
  UpdateAssignmentReviewAiSettingsRequest,
} from "./types";

export const assignmentReviewAiSettingsQueryKeys = {
  baseKey: "assignment-review-ai-settings",
  all: () => [assignmentReviewAiSettingsQueryKeys.baseKey, "all"] as const,
};

export const assignmentReviewAiSettingsApi = {
  get: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<AssignmentReviewAiSettingsDto>>(
      `/assignment-review/admin/ai-settings/`,
      { signal },
    );
    return res.data;
  },

  update: async (request: UpdateAssignmentReviewAiSettingsRequest) => {
    const res = await apiClient.put<Envelope<void>>(
      `/assignment-review/admin/ai-settings/`,
      request,
    );
    return res.data;
  },
};

export const assignmentReviewAiSettingsQueryOptions = () =>
  queryOptions({
    queryKey: assignmentReviewAiSettingsQueryKeys.all(),
    queryFn: ({ signal }) => assignmentReviewAiSettingsApi.get({ signal }),
    select: (data) => data.result!,
    staleTime: 60_000,
  });
