"use client";

import { type TrainerTopicListItem } from "@/entities/trainer-topic";
import { MyMistakes } from "@/features/trainer-my-mistakes";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import Link from "next/link";

interface HubMistakesTabProps {
  isAuthenticated: boolean;
  /** Карта topicId → тема (для названия темы в строке ошибки). */
  topicMap: Map<string, TrainerTopicListItem>;
  /** Если выбран трек, показываем только ошибки его тем. */
  topicIds?: Set<string> | null;
  /** Темы выбранного трека ещё загружаются. */
  isScopeLoading?: boolean;
  /** «Доучить» — хаб стартует ТЕСТ (review-сессию) по этим вопросам и навигирует. */
  onLaunchTest: (questionIds: string[]) => void;
  /** Идёт старт сессии — блокируем кнопки. */
  isLaunching?: boolean;
}

/**
 * Вкладка «Ошибки» хаба (#568, тест-разворот): кросс-тематический список вопросов
 * со статусом WRONG/REVIEW (`my-mistakes`) → «Доучить» запускает ТЕСТ по выбранному
 * числу ошибок. Аноним — приглашение войти (own-data, auth-gated на бэке).
 */
export function HubMistakesTab({
  isAuthenticated,
  topicMap,
  topicIds,
  isScopeLoading = false,
  onLaunchTest,
  isLaunching = false,
}: HubMistakesTabProps) {
  if (!isAuthenticated) {
    return (
      <EmptyState
        icon={Icons.target}
        variant="card"
        title="Ошибки после входа"
        description="Войди, чтобы видеть вопросы, в которых ошибся, и доучивать их карточками."
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

  if (isScopeLoading) {
    return <MistakesScopeSkeleton />;
  }

  return (
    <MyMistakes
      topicMap={topicMap}
      topicIds={topicIds ?? undefined}
      onLaunchTest={onLaunchTest}
      isLaunching={isLaunching}
    />
  );
}

function MistakesScopeSkeleton() {
  return (
    <div className="space-y-5">
      <Skeleton className="h-11 w-full max-w-md rounded-lg" />
      <div className="space-y-2">
        {Array.from({ length: 4 }).map((_, index) => (
          <Skeleton key={index} className="h-20 w-full rounded-lg" />
        ))}
      </div>
    </div>
  );
}
