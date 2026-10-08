"use client";

import { quizQueryOptions } from "@/entities/quiz";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { QuizAttemptPanel } from "./quiz-attempt-panel";

interface MaterialQuizBlockProps {
  materialId: string;
  className?: string;
}

/**
 * Блок «Проверь себя» на странице материала (только для auth-юзеров — гейт на
 * стороне виджета). Материал без опубликованного квиза → бэкенд отвечает 404 →
 * блок не рендерится. Core-логика попыток (форма/разбор/история best-last)
 * живёт в {@link QuizAttemptPanel} — общий с страницей `/quizzes/[quizId]`
 * (ST-16 #495). Issue #471.
 */
export function MaterialQuizBlock({ materialId, className }: MaterialQuizBlockProps) {
  const { data: quiz } = useQuery(quizQueryOptions.materialQuizOptions(materialId));

  if (!quiz) return null;

  return (
    <section className={className}>
      <div className="mb-4 flex flex-wrap items-baseline gap-x-2 gap-y-1">
        <div className="flex items-center gap-2">
          <Icons.listChecks size={16} className="text-primary" />
          <h2 className="text-sm font-semibold">Проверь себя</h2>
        </div>
        <span className="text-sm text-muted-foreground">{quiz.title}</span>
      </div>
      <QuizAttemptPanel quiz={quiz} />
    </section>
  );
}
