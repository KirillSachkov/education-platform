"use client";

import {
  quizQueryOptions,
  type QuizAttemptResultDto,
  type QuizStudentDto,
  type SubmitQuizAnswerItem,
} from "@/entities/quiz";
import { cn } from "@/shared/lib/css";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { resolveUnlockHref } from "@/shared/lib/lock-copy";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import Link from "next/link";
import { useState } from "react";
import { isQuizChangedError, useSubmitQuizAttempt } from "../model/use-submit-quiz-attempt";
import { QuizAttemptForm } from "./quiz-attempt-form";
import { QuizAttemptReview } from "./quiz-attempt-review";

type ViewOverride =
  | { kind: "form" }
  | { kind: "review"; result: QuizAttemptResultDto }
  | { kind: "quiz-changed" }
  | null;

interface QuizAttemptPanelProps {
  quiz: QuizStudentDto;
  className?: string;
}

/**
 * Core-блок прохождения квиза (вынесен из MaterialQuizBlock, ST-16 #495):
 * форма попытки + full-reveal разбор + история best/last с «Показать разбор» /
 * «Пройти заново». Используется блоком «Проверь себя» на материале и
 * студенческой страницей `/quizzes/[quizId]`.
 *
 * Анониму (PUBLIC-квиз) вопросы видны, но submit заблокирован — под кнопкой
 * CTA «Войти, чтобы сохранить результат» (anon-попыток в этом флоу нет).
 */
export function QuizAttemptPanel({ quiz, className }: QuizAttemptPanelProps) {
  const { status: sessionStatus } = useSession();
  const isAuthenticated = sessionStatus === "authenticated";

  const { data: attempts, isLoading: isAttemptsLoading } = useQuery({
    ...quizQueryOptions.myAttemptsOptions(quiz.id),
    enabled: isAuthenticated,
  });

  const [override, setOverride] = useState<ViewOverride>(null);
  const submitMutation = useSubmitQuizAttempt(quiz.id);

  if (sessionStatus === "loading") {
    return (
      <div className={cn("flex items-center gap-2 text-sm text-muted-foreground", className)}>
        <Icons.loading className="size-4 animate-spin" />
        Загрузка…
      </div>
    );
  }

  if (!isAuthenticated) {
    // Страница рендерится client-side после загрузки квиза, поэтому
    // window здесь всегда доступен (guard — на всякий случай для SSR).
    const returnTo = typeof window !== "undefined" ? window.location.pathname : null;
    const loginHref = resolveUnlockHref({ lockReason: "anonymous", returnTo }) ?? "/login";
    return (
      <div className={className}>
        <QuizAttemptForm
          quiz={quiz}
          onSubmit={() => {}}
          isPending={false}
          submitDisabled
          belowSubmit={
            <Button asChild variant="outline" size="sm">
              <Link href={loginHref}>
                <Icons.login className="size-3.5" />
                Войти, чтобы сохранить результат
              </Link>
            </Button>
          }
        />
      </div>
    );
  }

  const handleSubmit = (answers: SubmitQuizAnswerItem[]) => {
    submitMutation.mutate(
      { answers },
      {
        onSuccess: (result) => setOverride({ kind: "review", result }),
        onError: (error) => {
          // Автор отредактировал тест во время прохождения — id вопросов разъехались,
          // попытка не сохранена. Не рисуем 0%-разбор, показываем баннер с перезагрузкой.
          if (isQuizChangedError(error)) setOverride({ kind: "quiz-changed" });
        },
      },
    );
  };

  let body: React.ReactNode;
  if (override?.kind === "quiz-changed") {
    body = (
      <div className="flex flex-col items-start gap-3 rounded-xl border border-amber-500/40 bg-amber-500/10 p-4">
        <div className="flex items-start gap-3">
          <Icons.warning className="mt-0.5 size-5 shrink-0 text-amber-600 dark:text-amber-400" />
          <div className="space-y-1">
            <p className="text-sm font-semibold">Тест обновлён автором</p>
            <p className="text-xs text-muted-foreground">
              Пока вы проходили тест, автор изменил вопросы. Обновите страницу и пройдите заново —
              ваши ответы не сохранились.
            </p>
          </div>
        </div>
        <Button size="sm" onClick={() => window.location.reload()}>
          <Icons.refresh className="size-3.5" />
          Обновить страницу
        </Button>
      </div>
    );
  } else if (override?.kind === "review") {
    body = (
      <QuizAttemptReview
        quiz={quiz}
        result={override.result}
        onRetake={() => setOverride({ kind: "form" })}
      />
    );
  } else if (override?.kind === "form" || (!isAttemptsLoading && !attempts?.last)) {
    body = (
      <QuizAttemptForm
        quiz={quiz}
        onSubmit={handleSubmit}
        isPending={submitMutation.isPending}
        allowCheckAnswers
      />
    );
  } else if (isAttemptsLoading) {
    body = (
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Icons.loading className="size-4 animate-spin" />
        Загрузка результатов…
      </div>
    );
  } else {
    // attempts.last гарантирован веткой выше.
    const best = attempts!.best!;
    const last = attempts!.last!;
    body = (
      <div className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-border/60 bg-card/50 p-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <p className="text-sm font-semibold">Лучший результат: {best.scorePercent}%</p>
            <Badge
              variant="outline"
              className={cn(
                "text-[11px]",
                best.passed
                  ? "border-green/50 text-green"
                  : "border-destructive/50 text-destructive",
              )}
            >
              {best.passed ? "пройдено" : "не пройдено"}
            </Badge>
          </div>
          <p className="text-xs text-muted-foreground">
            Последняя попытка: {last.scorePercent}% · {formatShortDateWithTime(last.submittedAt)}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setOverride({ kind: "review", result: last })}
          >
            <Icons.view className="size-3.5" />
            Показать разбор
          </Button>
          <Button size="sm" onClick={() => setOverride({ kind: "form" })}>
            <Icons.refresh className="size-3.5" />
            Пройти заново
          </Button>
        </div>
      </div>
    );
  }

  return <div className={className}>{body}</div>;
}
