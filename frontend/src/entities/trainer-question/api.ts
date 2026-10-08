import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  TrainerMistakeItem,
  TrainerMistakesFilter,
  TrainerQuestionList,
  TrainerQuestionsFilter,
  TrainerSrsDueItem,
} from "./types";

/** Порядок сложности для сортировки списков: джун → миддл → сеньор (#568). */
const DIFFICULTY_RANK: Record<string, number> = { JUNIOR: 0, MIDDLE: 1, SENIOR: 2 };

/** Стабильный `select`: вопросы темы всегда отсортированы джун → миддл → сеньор (везде). */
function selectSortedByDifficulty(data: TrainerQuestionList): TrainerQuestionList {
  return {
    ...data,
    items: [...data.items].sort(
      (a, b) =>
        (DIFFICULTY_RANK[a.difficulty ?? ""] ?? 99) - (DIFFICULTY_RANK[b.difficulty ?? ""] ?? 99),
    ),
  };
}

export const trainerQuestionsApi = {
  /**
   * Список вопросов охвата (тема): метаданные + персональный статус изучения +
   * закладка, БЕЗ ключа грейдинга. Опц. фасет-фильтры (in-memory на бэке).
   * Метаданные не gated (free-тема — анонимный просмотр для SEO); PRO-тема без
   * доступа → `isLocked=true`, items без ответов.
   */
  getTopicQuestions: async (
    topicId: string,
    filter: TrainerQuestionsFilter = {},
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerQuestionList> => {
    const params = new URLSearchParams();
    if (filter.difficulty) params.set("difficulty", filter.difficulty);
    if (filter.status) params.set("status", filter.status);
    if (filter.type) params.set("type", filter.type);
    if (filter.tag) params.set("tag", filter.tag);
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerQuestionList>>(
      `/trainer/topics/${topicId}/questions/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result!;
  },

  /**
   * Кросс-тематическая SRS-очередь «на повтор сегодня» вызывающего:
   * study-state'ы с `nextDueAt <= now`, most-due first. Own-data. `limit` 1..200.
   */
  getSrsDue: async (
    limit?: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerSrsDueItem[]> => {
    const params = new URLSearchParams();
    if (limit != null) params.set("limit", String(limit));
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerSrsDueItem[]>>(
      `/trainer/srs/due/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /**
   * Кросс-тематические «Мои ошибки» (статус WRONG/REVIEW), most-recent-wrong
   * first. Own-data. Опц. фильтр `topicId`/`difficulty`/`limit`.
   */
  getMistakes: async (
    filter: TrainerMistakesFilter = {},
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerMistakeItem[]> => {
    const params = new URLSearchParams();
    if (filter.topicId) params.set("topicId", filter.topicId);
    if (filter.difficulty) params.set("difficulty", filter.difficulty);
    if (filter.limit != null) params.set("limit", String(filter.limit));
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerMistakeItem[]>>(
      `/trainer/mistakes/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? [];
  },
};

export const trainerQuestionsQueryOptions = {
  listBaseKey: "trainer-questions",
  srsBaseKey: "trainer-srs-due",
  mistakesBaseKey: "trainer-mistakes",

  listKey: (topicId: string, filter: TrainerQuestionsFilter = {}) =>
    [
      trainerQuestionsQueryOptions.listBaseKey,
      topicId,
      filter.difficulty ?? null,
      filter.status ?? null,
      filter.type ?? null,
      filter.tag ?? null,
    ] as const,
  srsKey: (limit?: number) =>
    [trainerQuestionsQueryOptions.srsBaseKey, limit ?? null] as const,
  mistakesKey: (filter: TrainerMistakesFilter = {}) =>
    [
      trainerQuestionsQueryOptions.mistakesBaseKey,
      filter.topicId ?? null,
      filter.difficulty ?? null,
      filter.limit ?? null,
    ] as const,

  /** Список вопросов охвата темы (опц. фасет-фильтры). Метаданные не gated. */
  listOptions: (topicId: string, filter: TrainerQuestionsFilter = {}) =>
    queryOptions({
      queryKey: trainerQuestionsQueryOptions.listKey(topicId, filter),
      queryFn: ({ signal }) =>
        trainerQuestionsApi.getTopicQuestions(topicId, filter, { signal }),
      staleTime: 30_000,
      select: selectSortedByDifficulty,
    }),

  /** SRS-очередь «на повтор сегодня». Гейтить auth. */
  srsOptions: (limit?: number) =>
    queryOptions({
      queryKey: trainerQuestionsQueryOptions.srsKey(limit),
      queryFn: ({ signal }) => trainerQuestionsApi.getSrsDue(limit, { signal }),
      staleTime: 30_000,
    }),

  /** «Мои ошибки» (опц. фильтр). Гейтить auth. */
  mistakesOptions: (filter: TrainerMistakesFilter = {}) =>
    queryOptions({
      queryKey: trainerQuestionsQueryOptions.mistakesKey(filter),
      queryFn: ({ signal }) => trainerQuestionsApi.getMistakes(filter, { signal }),
      staleTime: 30_000,
    }),
};
