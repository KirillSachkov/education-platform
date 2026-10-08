import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AttachMaterialToModuleRequest,
  AttachQuizToModuleRequest,
  CreateModuleLessonRequest,
  ModuleDetailDto,
  ModuleOverviewDto,
  MoveModuleItemRequest,
  TransferModuleItemRequest,
  UpdateLessonRequest,
  UpdateModuleRequest,
} from "./types";

export const modulesApi = {
  getModuleDetail: async (moduleId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<ModuleDetailDto>>(
      `/modules/${moduleId}/detail`,
      { signal },
    );
    return res.data;
  },

  updateModule: async ({ moduleId, request }: { moduleId: string; request: UpdateModuleRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/modules/${moduleId}`,
      request,
    );
    return res.data;
  },

  createLesson: async ({ moduleId, request }: { moduleId: string; request: CreateModuleLessonRequest }) => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/lessons/`,
      request,
    );
    return res.data;
  },

  moveItem: async ({ moduleId, referenceId, request }: { moduleId: string; referenceId: string; request: MoveModuleItemRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/modules/${moduleId}/items/${referenceId}/move`,
      request,
    );
    return res.data;
  },

  detachItem: async ({ moduleId, referenceId }: { moduleId: string; referenceId: string }) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/modules/${moduleId}/items/${referenceId}`,
    );
    return res.data;
  },

  updateLesson: async ({ lessonId, request }: { lessonId: string; request: UpdateLessonRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/lessons/${lessonId}`,
      request,
    );
    return res.data;
  },

  publishModule: async (moduleId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/publish`,
    );
    return res.data.result!;
  },

  archiveModule: async (moduleId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/archive`,
    );
    return res.data.result!;
  },

  restoreModule: async (moduleId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/restore`,
    );
    return res.data.result!;
  },

  attachIssue: async ({ moduleId, issueId }: { moduleId: string; issueId: string }) => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/issues/${issueId}`,
    );
    return res.data;
  },

  attachMaterial: async ({
    moduleId,
    request,
  }: {
    moduleId: string;
    request: AttachMaterialToModuleRequest;
  }) => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/materials/`,
      request,
    );
    return res.data;
  },

  /** Привязка квиза к модулю (ST-12 #492) — возвращает id созданного module_item. */
  attachQuiz: async ({
    moduleId,
    request,
  }: {
    moduleId: string;
    request: AttachQuizToModuleRequest;
  }) => {
    const res = await apiClient.post<Envelope<string>>(
      `/modules/${moduleId}/quizzes/`,
      request,
    );
    return res.data;
  },

  transferItem: async ({ sourceModuleId, referenceId, request }: { sourceModuleId: string; referenceId: string; request: TransferModuleItemRequest }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/modules/${sourceModuleId}/items/${referenceId}/transfer`,
      request,
    );
    return res.data;
  },

  updateItemViewPriority: async ({ moduleId, referenceId, viewPriority }: { moduleId: string; referenceId: string; viewPriority: string }) => {
    const res = await apiClient.patch<Envelope<string>>(
      `/modules/${moduleId}/items/${referenceId}/priority`,
      { viewPriority },
    );
    return res.data;
  },

  getModuleOverview: async (moduleId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<ModuleOverviewDto>>(
      `/modules/${moduleId}/overview`,
      { signal },
    );
    return res.data;
  },
};

export const modulesQueryOptions = {
  baseKey: "modules",
};

export const moduleDetailQueryOptions = (moduleId: string) =>
  queryOptions({
    queryKey: [modulesQueryOptions.baseKey, moduleId, "detail"],
    queryFn: ({ signal }) => modulesApi.getModuleDetail(moduleId, { signal }),
    select: (data) => data.result!,
    enabled: !!moduleId,
  });

export const moduleOverviewQueryOptions = (moduleId: string) =>
  queryOptions({
    queryKey: [modulesQueryOptions.baseKey, moduleId, "overview"],
    queryFn: ({ signal }) => modulesApi.getModuleOverview(moduleId, { signal }),
    select: (data) => data.result!,
    enabled: !!moduleId,
  });
