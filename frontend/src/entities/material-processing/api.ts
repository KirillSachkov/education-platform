import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AiModelSettingsDto,
  GenerateVideoContentRequest,
  GenerateVideoContentResponse,
  GenerateVideoTimecodesResponse,
  GetActiveAiJobsResponse,
  GetAiUsageResponse,
  GetVideoTimecodesResponse,
  UpdateAiModelSettingsRequest,
} from "./types";

export const materialProcessingApi = {
  getVideoTimecodes: async (
    videoId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<GetVideoTimecodesResponse> => {
    const res = await apiClient.get<Envelope<GetVideoTimecodesResponse>>(
      `/material-processing/videos/${videoId}/`,
      { signal },
    );
    return res.data.result!;
  },

  generateTimecodes: async (
    videoId: string,
    options: { modelOverride?: string } = {},
  ): Promise<GenerateVideoTimecodesResponse> => {
    const res = await apiClient.post<Envelope<GenerateVideoTimecodesResponse>>(
      `/material-processing/videos/${videoId}/timecode-generations/`,
      undefined,
      {
        params: options.modelOverride
          ? { modelOverride: options.modelOverride }
          : undefined,
      },
    );
    return res.data.result!;
  },

  generateContent: async ({
    videoId,
    request,
  }: {
    videoId: string;
    request: GenerateVideoContentRequest;
  }): Promise<GenerateVideoContentResponse> => {
    const res = await apiClient.post<Envelope<GenerateVideoContentResponse>>(
      `/material-processing/videos/${videoId}/content-generations/`,
      request,
    );
    return res.data.result!;
  },

  // Admin-only: cumulative AI pipeline usage за окно `days` (default 7, max 90).
  getAiUsage: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<GetAiUsageResponse> => {
    const res = await apiClient.get<Envelope<GetAiUsageResponse>>(
      `/material-processing/admin/ai-usage`,
      { signal, params: { days } },
    );
    return res.data.result!;
  },

  // Admin-only: effective AI model settings (DB override or appsettings fallback).
  getAiModelSettings: async (
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AiModelSettingsDto> => {
    const res = await apiClient.get<Envelope<AiModelSettingsDto>>(
      `/material-processing/admin/ai-settings/`,
      { signal },
    );
    return res.data.result!;
  },

  updateAiModelSettings: async (
    request: UpdateAiModelSettingsRequest,
  ): Promise<AiModelSettingsDto> => {
    const res = await apiClient.put<Envelope<AiModelSettingsDto>>(
      `/material-processing/admin/ai-settings/`,
      request,
    );
    return res.data.result!;
  },

  getActiveJobs: async (
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<GetActiveAiJobsResponse> => {
    const res = await apiClient.get<Envelope<GetActiveAiJobsResponse>>(
      `/material-processing/jobs/active/`,
      { signal },
    );
    return res.data.result!;
  },
};

export const materialProcessingQueryOptions = {
  baseKey: "material-processing",

  video: (videoId: string) =>
    queryOptions({
      queryKey: [materialProcessingQueryOptions.baseKey, "video", videoId] as const,
      queryFn: ({ signal }) =>
        materialProcessingApi.getVideoTimecodes(videoId, { signal }),
      enabled: !!videoId,
      // Issue #188: при возврате на страницу материала после получения
      // notification «транскрипция завершена» (toast от global activeJobs
      // tracker'а) панель видела stale-статус из 60s глобального staleTime.
      // На revisit'е активный job уже исчез из activeJobs — useEffect внутри
      // VideoAiProcessingPanel не триггерит invalidate (нет diff в key).
      // Делаем query stale at zero + refetch on mount/focus, чтобы каждый
      // mount панели подтягивал свежий статус. Backend cache'ит на 2 min
      // через CachedMaterialProcessingClient, так что cost этого — копейки.
      staleTime: 0,
      refetchOnMount: "always",
      refetchOnWindowFocus: true,
    }),

  aiUsage: (days: number) =>
    queryOptions({
      queryKey: [materialProcessingQueryOptions.baseKey, "ai-usage", days] as const,
      // React Query throws if a queryFn resolves to `undefined`. The envelope's
      // `result` is `T | null`, and an empty-usage window can surface as a
      // missing field at runtime — coerce to `null` (consumer already guards
      // with `data?.rows ?? []`).
      queryFn: async ({ signal }) =>
        (await materialProcessingApi.getAiUsage(days, { signal })) ?? null,
    }),

  aiModelSettings: () =>
    queryOptions({
      queryKey: [materialProcessingQueryOptions.baseKey, "ai-model-settings"] as const,
      queryFn: ({ signal }) => materialProcessingApi.getAiModelSettings({ signal }),
    }),

  activeJobs: () =>
    queryOptions({
      queryKey: [materialProcessingQueryOptions.baseKey, "active-jobs"] as const,
      queryFn: ({ signal }) => materialProcessingApi.getActiveJobs({ signal }),
      // НЕТ refetchInterval здесь — polling drive'ит leader-only через
      // `useAiJobsLeaderPoll` (widgets/ai-jobs-tracker), который через
      // Web Locks API выбирает ОДНУ tab на user'а делать реальный poll и
      // broadcasts snapshots остальным tabs через BroadcastChannel.
      // Любой `useQuery({...activeJobs()})` сам не poll'ит — только читает
      // shared cache.
      staleTime: 4000,
    }),
};
