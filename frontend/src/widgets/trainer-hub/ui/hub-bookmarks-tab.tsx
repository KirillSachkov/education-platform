"use client";

import { TrainerBookmarksList } from "@/features/trainer-bookmarks";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import Link from "next/link";

interface HubBookmarksTabProps {
  isAuthenticated: boolean;
  topicIds?: ReadonlySet<string> | null;
  isScopeLoading?: boolean;
  /** «Пройти тест» по закладке — хаб стартует review-сессию и навигирует. */
  onLaunchTest: (questionIds: string[]) => void;
  /** Идёт старт сессии — блокируем кнопки. */
  isLaunching?: boolean;
}

/**
 * Вкладка «Закладки» хаба (#568): сохранённые вопросы тренажёра вызывающего.
 * Аноним — приглашение войти. Логика списка/удаления — в фиче `trainer-bookmarks`.
 */
export function HubBookmarksTab({
  isAuthenticated,
  topicIds,
  isScopeLoading = false,
  onLaunchTest,
  isLaunching = false,
}: HubBookmarksTabProps) {
  if (!isAuthenticated) {
    return (
      <EmptyState
        icon={Icons.bookmark}
        variant="card"
        title="Закладки после входа"
        description="Войди, чтобы сохранять вопросы из тренировок и возвращаться к ним."
        action={
          <Button asChild>
            <Link href={`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`}>
              Войти
            </Link>
          </Button>
        }
      />
    );
  }

  return (
    <TrainerBookmarksList
      topicIds={topicIds}
      isScopeLoading={isScopeLoading}
      onLaunchTest={onLaunchTest}
      isLaunching={isLaunching}
    />
  );
}
