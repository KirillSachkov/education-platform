import type { Envelope } from "./errors";

export type PaginationResponse<T> = {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

/**
 * getNextPageParam для page-based infinite-query над Envelope<PaginationResponse<T>>.
 * Возвращает номер следующей страницы или undefined на последней. Единый источник, чтобы
 * все user/list-компоненты не переписывали page-логику по-своему (источник off-by-one).
 */
export function nextPageParamFromPagination<T>(
  lastPage: Envelope<PaginationResponse<T>>,
): number | undefined {
  const result = lastPage.result;
  if (!result || result.totalPages === 0) return undefined;
  return result.page < result.totalPages ? result.page + 1 : undefined;
}
