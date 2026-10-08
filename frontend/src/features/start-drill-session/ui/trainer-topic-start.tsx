"use client";

import { trainerTopicsQueryOptions, type TrainerTopicListItem } from "@/entities/trainer-topic";
import { TRAINER_DEFAULT_QUESTION_COUNT } from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { LockCallout } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useState } from "react";
import { useStartDrill } from "../model/use-start-drill";

const QUESTION_COUNT_CHOICES = [5, 10, 15] as const;

interface TrainerTopicStartProps {
  slug: string;
}

/**
 * Панель темы `/trainer/topics/{slug}` (#568): метаданные темы + выбор длины
 * DRILL → «Начать тренировку» → редирект на `/trainer/session/{id}`. Аноним
 * видит teaser + CTA «Войти»; полностью платная тема — lock-callout «Выбрать
 * план». Тему резолвим из общего списка тем (slug → topic).
 */
export function TrainerTopicStart({ slug }: TrainerTopicStartProps) {
  const session = useSession();
  const isAuthenticated = session.status === "authenticated";
  const topicsQuery = useQuery(trainerTopicsQueryOptions.topicsOptions());

  if (topicsQuery.isPending) {
    return (
      <div className="mx-auto w-full max-w-2xl space-y-4">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="h-8 w-3/4" />
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-11 w-48" />
      </div>
    );
  }

  if (topicsQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        className="mx-auto max-w-2xl"
        title="Не удалось загрузить тему"
        description={getErrorMessage(topicsQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => topicsQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const topic = topicsQuery.data.find((candidate) => candidate.slug === slug);

  if (!topic) {
    return (
      <EmptyState
        icon={Icons.searchEmpty}
        variant="card"
        className="mx-auto max-w-2xl"
        title="Тема не найдена"
        description="Возможно, она ещё не опубликована."
        action={
          <Button asChild variant="outline">
            <Link href={routes.trainer}>
              <Icons.chevronLeft className="size-4" />К темам
            </Link>
          </Button>
        }
      />
    );
  }

  return (
    <TopicStartPanel topic={topic} isAuthenticated={isAuthenticated} />
  );
}

function TopicStartPanel({
  topic,
  isAuthenticated,
}: {
  topic: TrainerTopicListItem;
  isAuthenticated: boolean;
}) {
  const router = useRouter();
  const startDrill = useStartDrill();
  const [questionCount, setQuestionCount] = useState<number>(TRAINER_DEFAULT_QUESTION_COUNT);

  const handleStart = () => {
    if (startDrill.isPending) return;
    startDrill.mutate(
      { topicId: topic.id, questionCount },
      { onSuccess: (created) => router.push(routes.trainerSession(created.id)) },
    );
  };

  return (
    <div className="mx-auto w-full max-w-2xl space-y-6">
      <Button asChild variant="ghost" size="sm" className="w-fit -ml-2 text-muted-foreground">
        <Link href={routes.trainer}>
          <Icons.chevronLeft className="size-4" />К темам
        </Link>
      </Button>

      <header className="space-y-2">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {topic.area}
        </p>
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">{topic.title}</h1>
        {topic.description && (
          <p className="text-sm text-muted-foreground sm:text-base">{topic.description}</p>
        )}
        {isAuthenticated && topic.answersCount > 0 && (
          <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
            <Icons.trending className="size-4" />
            Твой mastery по теме:{" "}
            <span className="font-mono font-medium tabular-nums text-foreground/80">
              {topic.masteryPercent}%
            </span>
          </p>
        )}
      </header>

      {topic.isLocked ? (
        <div className="rounded-2xl border border-border/60 bg-card p-5 sm:p-6">
          <LockCallout reason={topic.lockReason ?? "pro_required"} ctaHref={routes.trainerPro} />
        </div>
      ) : !isAuthenticated ? (
        <div className="rounded-2xl border border-border/60 bg-card p-5 text-center sm:p-6">
          <p className="text-sm text-muted-foreground">
            Войди, чтобы начать тренировку по этой теме.
          </p>
          <Button asChild className="mt-4">
            <Link
              href={`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainerTopic(topic.slug))}`}
            >
              <Icons.login className="size-4" />
              Войти и начать
            </Link>
          </Button>
        </div>
      ) : (
        <div className="space-y-4 rounded-2xl border border-border/60 bg-card p-5 sm:p-6">
          <div className="space-y-2">
            <p className="text-sm font-medium">Сколько вопросов?</p>
            <div className="flex flex-wrap gap-2">
              {QUESTION_COUNT_CHOICES.map((count) => (
                <button
                  key={count}
                  type="button"
                  onClick={() => setQuestionCount(count)}
                  aria-pressed={questionCount === count}
                  className={cn(
                    "min-h-[44px] min-w-[64px] rounded-lg border px-4 text-sm font-medium tabular-nums transition-colors",
                    questionCount === count
                      ? "border-primary bg-primary/10 text-primary"
                      : "border-border/60 bg-card/50 text-foreground/80 hover:bg-accent/40",
                  )}
                >
                  {count}
                </button>
              ))}
            </div>
            <p className="text-xs text-muted-foreground">
              Вопросы выбираются случайно из банка темы — если в банке меньше, получишь все.
            </p>
          </div>

          <Button onClick={handleStart} disabled={startDrill.isPending} className="w-full sm:w-auto">
            {startDrill.isPending ? (
              <Icons.loading className="size-4 animate-spin" />
            ) : (
              <Icons.target className="size-4" />
            )}
            Начать тренировку
          </Button>
        </div>
      )}
    </div>
  );
}
