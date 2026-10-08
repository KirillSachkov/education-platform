"use client";

import { trainerQuestionsQueryOptions } from "@/entities/trainer-question";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";

/** Сколько due-вопросов тянем для баннера/очереди повтора (cap бэка — 200). */
const SRS_DUE_LIMIT = 50;

interface SrsReviewBannerProps {
  /** Гейтим запрос — аноним SRS не имеет (own-data, auth-gated на бэке). */
  isAuthenticated: boolean;
  /** Если выбран трек в хабе, показываем due-вопросы только его тем. */
  topicIds?: Set<string> | null;
  /** Темы выбранного трека ещё загружаются. */
  isScopeLoading?: boolean;
  /** Запустить повтор: ТЕСТ (review-сессия) по due-вопросам (хаб стартует и навигирует). */
  onStartReview: (questionIds: string[]) => void;
  /** Идёт старт сессии — блокируем кнопку. */
  isLaunching?: boolean;
}

/**
 * Баннер «На повтор сегодня: N» (#568, тест-разворот) над табами хаба. Тянет
 * кросс-тематическую SRS-очередь; рендерится ТОЛЬКО когда N>0 (иначе `null`).
 * «Повторить» запускает ТЕСТ по due-вопросам (не флеш-карты). Де-иконенный
 * минимализм, mobile-first. Спиннер/ошибку не шумит — молча скрывается.
 */
export function SrsReviewBanner({
  isAuthenticated,
  topicIds,
  isScopeLoading = false,
  onStartReview,
  isLaunching = false,
}: SrsReviewBannerProps) {
  const dueQuery = useQuery({
    ...trainerQuestionsQueryOptions.srsOptions(SRS_DUE_LIMIT),
    enabled: isAuthenticated,
  });

  const due = topicIds
    ? (dueQuery.data ?? []).filter((item) => topicIds.has(item.topicId))
    : (dueQuery.data ?? []);
  if (!isAuthenticated || isScopeLoading || due.length === 0) {
    return null;
  }

  const questionIds = due.map((item) => item.questionId);

  return (
    <div className="flex flex-col gap-4 rounded-2xl border border-primary/25 bg-card/70 p-4 shadow-sm sm:flex-row sm:items-center sm:justify-between">
      <div className="flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
          <Icons.refresh className="size-5" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <p className="text-sm font-semibold">На повтор сегодня: {due.length}</p>
          <p className="text-sm leading-relaxed text-muted-foreground">
            Вопросы, которые пора освежить по интервальному повторению. Лучше закрыть их до
            новой симуляции.
          </p>
        </div>
      </div>
      <Button
        onClick={() => onStartReview(questionIds)}
        disabled={isLaunching}
        className="w-full shrink-0 sm:w-auto"
      >
        {isLaunching && <Icons.loading className="size-4 animate-spin" />}
        Повторить
        <Icons.chevronRight className="size-4" />
      </Button>
    </div>
  );
}
