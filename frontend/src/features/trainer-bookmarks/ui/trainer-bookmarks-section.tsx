"use client";

import { trainerSessionQueryOptions } from "@/entities/trainer-session";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useInfiniteQuery } from "@tanstack/react-query";
import Link from "next/link";
import { TrainerBookmarksList } from "./trainer-bookmarks-list";

/**
 * Секция «Из тренажёра» на глобальной странице «Сохранённое»: сохранённые вопросы
 * тренажёра рядом с курсовыми закладками. Скрывается целиком, если закладок нет
 * (чтобы не плодить пустые блоки). Список/удаление — в `TrainerBookmarksList`
 * (тот же запрос, React Query дедупит ключ). Анониму не показываем (запрос auth-gated).
 */
export function TrainerBookmarksSection() {
  const isAuthenticated = useIsAuthenticated();
  const bookmarksQuery = useInfiniteQuery({
    ...trainerSessionQueryOptions.bookmarksInfiniteOptions(),
    enabled: isAuthenticated,
  });

  // Прячем секцию пока пусто/грузится/ошибка/аноним — у курсовых закладок свой блок.
  if (!isAuthenticated || (bookmarksQuery.data?.pages[0]?.items.length ?? 0) === 0) {
    return null;
  }

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-lg font-semibold tracking-tight">Из тренажёра</h2>
        <Button asChild variant="ghost" size="sm" className="text-muted-foreground">
          <Link href={`${routes.trainer}?tab=bookmarks`}>
            Открыть
            <Icons.chevronRight className="size-4" />
          </Link>
        </Button>
      </div>
      <TrainerBookmarksList />
    </section>
  );
}
