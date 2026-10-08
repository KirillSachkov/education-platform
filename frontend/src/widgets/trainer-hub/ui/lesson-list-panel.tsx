"use client";

import {
  deriveLessons,
  trainerQuestionsQueryOptions,
  type TrainerLesson,
} from "@/entities/trainer-question";
import { useStartDrill } from "@/features/start-drill-session";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { getMasteryTone } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { LockCallout } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { deriveLessonProgress } from "../lib/lesson-progress";
import { useTrainerLoginGate } from "../lib/use-trainer-login-gate";

interface LessonListPanelProps {
  topicId: string;
  topicLocked: boolean;
}

/**
 * Под-режим «Тесты» вкладки «Изучение» (#568 Ф3): тема как упорядоченный набор
 * мини-тестов (юнитов) в стиле Duolingo. Деривим юниты из списка вопросов охвата
 * (`deriveLessons`), рендерим карточками J→M→S с цветовым акцентом уровня и
 * прогрессом «изучено». Первый незакрытый юнит подсвечиваем «Продолжить». Без
 * хард-гейта — любой юнит открывается. Клик → старт теста ровно по вопросам юнита
 * с раскрытием результатов после завершения. Locked-тема → CTA на планы. Mobile-first, ≥44px.
 */
export function LessonListPanel({ topicId, topicLocked }: LessonListPanelProps) {
  const router = useRouter();
  const { requireAuth } = useTrainerLoginGate();
  const startDrill = useStartDrill();
  const [pendingLessonId, setPendingLessonId] = useState<string | null>(null);
  const listQuery = useQuery(trainerQuestionsQueryOptions.listOptions(topicId));

  if (topicLocked) {
    return (
      <div className="rounded-xl border border-border/60 bg-card p-5 sm:p-6">
        <LockCallout reason="pro_required" ctaHref={routes.trainerPro} />
      </div>
    );
  }

  if (listQuery.isPending) {
    return <LessonListSkeleton />;
  }

  if (listQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить тесты"
        description={getErrorMessage(listQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => listQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const lessons = deriveLessons(listQuery.data.items);

  if (lessons.length === 0) {
    return (
      <EmptyState
        icon={Icons.listChecks}
        variant="dashed"
        title="В этой теме пока нет тестов"
        description="Вопросы ещё не добавлены — загляни позже или выбери другую тему."
      />
    );
  }

  // Первый незакрытый юнит — на нём показываем подсказку «Продолжить тут».
  const continueLessonId = lessons.find((lesson) => !lesson.isComplete)?.id ?? null;

  const handleStart = (lesson: TrainerLesson) => {
    if (!requireAuth()) return;
    if (startDrill.isPending) return;
    setPendingLessonId(lesson.id);
    startDrill.mutate(
      {
        topicId,
        questionIds: lesson.questionIds,
        revealPolicy: "END_OF_SESSION",
      },
      {
        onSuccess: (created) => router.push(routes.trainerSession(created.id)),
        onSettled: () => setPendingLessonId(null),
      },
    );
  };

  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">
        Тема разбита на короткие тесты по уровням. Отвечай на весь набор — результат и разбор
        откроются после завершения.
      </p>
      <ul className="space-y-2.5">
        {lessons.map((lesson) => (
          <LessonCard
            key={lesson.id}
            lesson={lesson}
            isContinue={lesson.id === continueLessonId}
            isPending={pendingLessonId === lesson.id}
            disabled={startDrill.isPending}
            onStart={() => handleStart(lesson)}
          />
        ))}
      </ul>
    </div>
  );
}

/** Цвет точки-акцента уровня юнита (J/M/S) — визуально различает карточки уровней. */
const LEVEL_DOT: Record<string, string> = {
  JUNIOR: "bg-green",
  MIDDLE: "bg-amber-500",
  SENIOR: "bg-destructive",
};

function LessonCard({
  lesson,
  isContinue,
  isPending,
  disabled,
  onStart,
}: {
  lesson: TrainerLesson;
  isContinue: boolean;
  isPending: boolean;
  disabled: boolean;
  onStart: () => void;
}) {
  const progress = deriveLessonProgress(lesson);
  const tone = getMasteryTone(progress.percent);
  const dotClass = LEVEL_DOT[lesson.level] ?? "bg-muted-foreground/40";

  return (
    <li>
      <button
        type="button"
        onClick={onStart}
        disabled={disabled}
        aria-label={`${lesson.title}: ${lesson.total} вопросов, освоено ${lesson.known} из ${lesson.total}`}
        className={cn(
          "group flex w-full items-center gap-3 rounded-xl border bg-card px-4 py-3 text-left sm:gap-4",
          // Только цветовые transition — БЕЗ transform/scale/shadow и пружинного easing
          // (это давало мерцание на ховере, как и у карточек тем #585).
          "transition-colors duration-200",
          "hover:border-border hover:bg-accent/20",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
          "disabled:pointer-events-none disabled:opacity-60",
          isContinue ? "border-primary/40" : "border-border/60",
        )}
      >
        <span
          aria-hidden="true"
          className={cn(
            "size-2.5 shrink-0 rounded-full",
            lesson.isComplete ? "bg-green" : dotClass,
          )}
        />

        {/* Контент: на десктопе одна строка (заголовок · длинная полоса · результат),
            на мобиле стек (заголовок, ниже полоса+результат) — чтобы карточка
            не оставляла половину ширины пустой. */}
        <div className="flex min-w-0 flex-1 flex-col gap-1.5 sm:flex-row sm:items-center sm:gap-4">
          <div className="flex min-w-0 items-center gap-2 sm:shrink-0">
            <p className="min-w-0 truncate text-sm font-semibold leading-snug" title={lesson.title}>
              {lesson.title}
            </p>
            {lesson.isComplete ? (
              <span className="inline-flex shrink-0 items-center gap-1 rounded-md bg-green/10 px-1.5 py-0.5 text-[11px] font-medium text-green">
                <Icons.check className="size-3" />
                Пройден
              </span>
            ) : (
              isContinue && (
                <span className="inline-flex shrink-0 items-center rounded-md bg-primary/10 px-1.5 py-0.5 text-[11px] font-medium text-primary">
                  Продолжить
                </span>
              )
            )}
          </div>

          {progress.kind === "untouched" ? (
            <span className="text-xs text-muted-foreground sm:ml-auto">{lesson.total} вопр.</span>
          ) : (
            <div className="flex min-w-0 flex-1 items-center gap-3">
              <div className="h-1.5 min-w-[48px] flex-1 overflow-hidden rounded-full bg-border/50">
                <div
                  className={cn("h-full rounded-full transition-[width] duration-500", tone)}
                  style={{ width: `${Math.max(progress.percent, 6)}%` }}
                />
              </div>
              <span className="shrink-0 whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                {progress.kind === "passed"
                  ? `Пройдено — ${progress.correct} из ${progress.total}`
                  : progress.kind === "attempted"
                    ? `Последняя попытка — ${progress.correct} из ${progress.total}`
                    : `Освоено ${progress.correct} из ${progress.total}`}
              </span>
            </div>
          )}
        </div>

        {isPending ? (
          <Icons.loading className="size-4 shrink-0 animate-spin text-muted-foreground" />
        ) : (
          <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/60" />
        )}
      </button>
    </li>
  );
}

function LessonListSkeleton() {
  return (
    <div className="space-y-3">
      <Skeleton className="h-4 w-3/4" />
      <div className="space-y-2.5">
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton key={index} className="h-[76px] w-full rounded-2xl" />
        ))}
      </div>
    </div>
  );
}
