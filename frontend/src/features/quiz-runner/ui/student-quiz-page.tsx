"use client";

import { quizQueryOptions } from "@/entities/quiz";
import { useIsAuthenticated } from "@/shared/auth";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { resolveSecondaryUnlockHref, resolveUnlockHref } from "@/shared/lib/lock-copy";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useQuery } from "@tanstack/react-query";
import { resolveQuizPageError } from "../model/quiz-page-error";
import { QuizAttemptPanel } from "./quiz-attempt-panel";

interface StudentQuizPageProps {
  quizId: string;
}

/**
 * Студенческая страница standalone-квиза `/quizzes/[quizId]` (ST-16 #495) —
 * универсальная цель quiz-строк программы курса и подборок. Состояния:
 * анонимный 401 → CTA «Войти» (callbackUrl назад); 403 → LockCallout;
 * 404 → EmptyState; доступный квиз → заголовок + мета + бейдж «Пройден»
 * (по best-попытке) + {@link QuizAttemptPanel}.
 */
export function StudentQuizPage({ quizId }: StudentQuizPageProps) {
  const isAuthenticated = useIsAuthenticated();
  const { data: quiz, isLoading, error } = useQuery(quizQueryOptions.studentQuizOptions(quizId));
  // Тот же query key, что внутри QuizAttemptPanel — react-query дедуплицирует.
  const { data: attempts } = useQuery({
    ...quizQueryOptions.myAttemptsOptions(quizId),
    enabled: isAuthenticated && !!quiz,
  });

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Icons.loading className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!quiz && error) {
    const state = resolveQuizPageError(error, isAuthenticated);

    if (state.kind === "not-found") {
      return (
        <div className="mx-auto max-w-md px-4 py-12 sm:py-16">
          <EmptyState
            icon={Icons.quiz}
            title="Тест не найден"
            description="Возможно, он был удалён или ещё не опубликован"
            variant="card"
          />
        </div>
      );
    }

    if (state.kind === "locked") {
      const returnTo = typeof window !== "undefined" ? window.location.pathname : null;
      return (
        <div className="mx-auto flex max-w-md flex-col items-stretch px-4 py-12 sm:py-16">
          <div className="rounded-2xl border border-border/60 bg-card/95 p-5 shadow-xl shadow-black/20">
            <LockCallout
              reason={state.reason}
              ctaHref={resolveUnlockHref({ lockReason: state.reason, returnTo })}
              secondaryCtaHref={resolveSecondaryUnlockHref({ lockReason: state.reason })}
            />
          </div>
        </div>
      );
    }

    return <ErrorCard error={error} className="py-16" />;
  }

  if (!quiz) return null;

  const best = attempts?.best ?? null;
  const questionsCount = quiz.questions.length;

  return (
    <div className="mx-auto max-w-3xl px-4 py-8 sm:px-6 sm:py-10">
      <header>
        <div className="mb-3 flex flex-wrap items-center gap-1.5">
          <span className="inline-flex items-center gap-1 rounded-md border border-violet-500/30 bg-violet-500/10 px-1.5 py-0.5 text-[11px] font-medium text-violet-400">
            <Icons.quiz className="size-3" />
            Тест
          </span>
          {best?.passed && (
            <Badge variant="outline" className={cn("border-green/50 text-green", "text-[11px]")}>
              <Icons.check className="size-3" />
              Пройден · лучший результат {best.scorePercent}%
            </Badge>
          )}
        </div>

        <h1 className="text-2xl font-bold leading-tight sm:text-3xl">{quiz.title}</h1>

        <p className="mt-2 text-sm text-muted-foreground tabular-nums">
          {questionsCount} {pluralize(questionsCount, "вопрос", "вопроса", "вопросов")} · проходной
          балл {quiz.passingScorePercent}%
        </p>
      </header>

      <QuizAttemptPanel quiz={quiz} className="mt-8" />
    </div>
  );
}
