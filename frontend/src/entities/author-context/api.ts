import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { AuthorContextDto } from "./types";

export const authorContextApi = {
  getMyAuthorContext: async (
    authorId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<AuthorContextDto>>(
      `/access/me/author-context/${authorId}/`,
      { signal },
    );
    return res.data;
  },
};

/**
 * Per-author проекция grant'ов текущего юзера (issue #83). Используется
 * в features/course-learning (use-resolved-course-access) для tier-aware доступа.
 */
export const myAuthorContextQueryOptions = (authorId: string | undefined) =>
  queryOptions({
    queryKey: ["access", "me", "author-context", authorId ?? ""],
    queryFn: ({ signal }) =>
      authorContextApi.getMyAuthorContext(authorId!, { signal }),
    select: (data) => data.result,
    enabled: Boolean(authorId),
    staleTime: 30_000,
  });
