import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AddTrainerTopicBankBody,
  CreateTrainerTopicBody,
  TrainerTopicAdmin,
  TrainerTopicBankAdmin,
  UpdateTrainerTopicBankBody,
  UpdateTrainerTopicBody,
} from "./types";

/** Фильтр admin-списка тем: опц. трек (зеркало `?trackId=`). */
export interface TrainerTopicsManageFilter {
  trackId?: string;
}

export const trainerAdminContentApi = {
  /**
   * Admin-список тем (включая DRAFT) + bankCount, опц. по треку. Admin-only.
   * Не скрывает неопубликованные (в отличие от студенческого `GET /trainer/topics`).
   */
  getTopics: async (
    filter: TrainerTopicsManageFilter = {},
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerTopicAdmin[]> => {
    const params = new URLSearchParams();
    if (filter.trackId) params.set("trackId", filter.trackId);
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerTopicAdmin[]>>(
      `/trainer/topics/manage/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /** Admin-карточка одной темы (включая DRAFT). Admin-only. */
  getTopic: async (
    topicId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerTopicAdmin> => {
    const res = await apiClient.get<Envelope<TrainerTopicAdmin>>(
      `/trainer/topics/${topicId}/manage/`,
      { signal },
    );
    return res.data.result!;
  },

  /** Банки вопросов темы (admin builder-проекция). Admin-only. */
  getTopicBanks: async (
    topicId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerTopicBankAdmin[]> => {
    const res = await apiClient.get<Envelope<TrainerTopicBankAdmin[]>>(
      `/trainer/topics/${topicId}/banks/`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /** Создать тему (DRAFT). Slug уникален. Возвращает id. Admin-only. */
  createTopic: async (body: CreateTrainerTopicBody): Promise<string> => {
    const res = await apiClient.post<Envelope<{ topicId: string }>>("/trainer/topics/", body);
    return res.data.result!.topicId;
  },

  /** Обновить детали темы + маппинг на курс. Slug immutable. Admin-only. */
  updateTopic: async (topicId: string, body: UpdateTrainerTopicBody): Promise<void> => {
    await apiClient.put<Envelope<unknown>>(`/trainer/topics/${topicId}/`, body);
  },

  /** Опубликовать тему (видна студентам). Admin-only. */
  publishTopic: async (topicId: string): Promise<void> => {
    await apiClient.post<Envelope<unknown>>(`/trainer/topics/${topicId}/publish/`);
  },

  /** Снять с публикации (DRAFT). Admin-only. */
  unpublishTopic: async (topicId: string): Promise<void> => {
    await apiClient.post<Envelope<unknown>>(`/trainer/topics/${topicId}/unpublish/`);
  },

  /** Удалить тему. 409 `trainer.topic.has.banks`, если есть привязанные банки. Admin-only. */
  deleteTopic: async (topicId: string): Promise<void> => {
    await apiClient.delete<Envelope<unknown>>(`/trainer/topics/${topicId}/`);
  },

  /** Создать пустой банк вопросов под темой (#623). Возвращает id. Admin-only. */
  addTopicBank: async (topicId: string, body: AddTrainerTopicBankBody): Promise<string> => {
    const res = await apiClient.post<Envelope<{ bankId: string }>>(
      `/trainer/topics/${topicId}/banks/`,
      body,
    );
    return res.data.result!.bankId;
  },

  /** Обновить банк (tier/difficulty/purpose). TopicId immutable. Admin-only. */
  updateTopicBank: async (bankId: string, body: UpdateTrainerTopicBankBody): Promise<void> => {
    await apiClient.put<Envelope<unknown>>(`/trainer/topic-banks/${bankId}/`, body);
  },

  /** Сменить только tier банка (FREE/PAID). Admin-only. */
  setTopicBankTier: async (bankId: string, tier: string): Promise<void> => {
    await apiClient.patch<Envelope<unknown>>(`/trainer/topic-banks/${bankId}/tier/`, { tier });
  },

  /** Удалить банк (вопросы каскадятся). Admin-only. */
  deleteTopicBank: async (bankId: string): Promise<void> => {
    await apiClient.delete<Envelope<unknown>>(`/trainer/topic-banks/${bankId}/`);
  },
};

export const trainerAdminContentQueryOptions = {
  topicsBaseKey: "trainer-admin-topics",
  topicBaseKey: "trainer-admin-topic",
  banksBaseKey: "trainer-admin-topic-banks",

  topicsKey: (filter: TrainerTopicsManageFilter = {}) =>
    [trainerAdminContentQueryOptions.topicsBaseKey, filter.trackId ?? null] as const,
  topicKey: (topicId: string) =>
    [trainerAdminContentQueryOptions.topicBaseKey, topicId] as const,
  banksKey: (topicId: string) =>
    [trainerAdminContentQueryOptions.banksBaseKey, topicId] as const,

  /** Admin-список тем (включая DRAFT), опц. фильтр по треку. */
  topicsOptions: (filter: TrainerTopicsManageFilter = {}) =>
    queryOptions({
      queryKey: trainerAdminContentQueryOptions.topicsKey(filter),
      queryFn: ({ signal }) => trainerAdminContentApi.getTopics(filter, { signal }),
      staleTime: 30_000,
    }),

  /** Admin-карточка одной темы (включая DRAFT). */
  topicOptions: (topicId: string) =>
    queryOptions({
      queryKey: trainerAdminContentQueryOptions.topicKey(topicId),
      queryFn: ({ signal }) => trainerAdminContentApi.getTopic(topicId, { signal }),
      staleTime: 30_000,
    }),

  /** Банки вопросов выбранной темы (admin builder-проекция). */
  banksOptions: (topicId: string) =>
    queryOptions({
      queryKey: trainerAdminContentQueryOptions.banksKey(topicId),
      queryFn: ({ signal }) => trainerAdminContentApi.getTopicBanks(topicId, { signal }),
      staleTime: 30_000,
    }),
};
