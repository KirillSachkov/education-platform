import { commentsApi } from "@/entities/comment";
import { useMutation } from "@tanstack/react-query";

/**
 * Двигает курсор последнего просмотра ленты автора. Вызывается на mount страницы
 * `/author/comments` — после визита все «непрочитанные» становятся прочитанными.
 * Best-effort: ошибки молча игнорируются, текущая страница не инвалидируется (чтобы
 * не «прыгала» из unread → read под курсором). Следующее открытие подтянет актуальный
 * unread-флаг.
 */
export function useMarkAuthorFeedViewed() {
  const mutation = useMutation({
    mutationFn: () => commentsApi.markAuthorFeedViewed(),
  });

  return {
    markViewed: mutation.mutate,
    isPending: mutation.isPending,
  };
}
