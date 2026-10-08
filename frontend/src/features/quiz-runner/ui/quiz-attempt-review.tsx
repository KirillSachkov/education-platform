"use client";

import {
  QuizOptionContent,
  QuizRichText,
  type QuizAttemptQuestionResultDto,
  type QuizAttemptResultDto,
  type QuizQuestionStudentDto,
  type QuizStudentDto,
} from "@/entities/quiz";
import { cn } from "@/shared/lib/css";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";

interface QuizAttemptReviewProps {
  quiz: QuizStudentDto;
  result: QuizAttemptResultDto;
  onRetake: () => void;
  retakeLabel?: string;
}

/**
 * Разбор попытки (full-reveal): score-баннер с проходным баллом, per-вопрос
 * раскраска выбранных/правильных вариантов, для OPEN_TEXT — ответ студента и
 * эталон («самопроверка», в score не входит). Issue #471.
 */
export function QuizAttemptReview({ quiz, result, onRetake, retakeLabel }: QuizAttemptReviewProps) {
  const questionById = new Map(quiz.questions.map((question) => [question.id, question]));

  return (
    <div className="space-y-6">
      <div
        className={cn(
          "flex flex-wrap items-center justify-between gap-3 rounded-xl border p-4",
          result.passed ? "border-green/40 bg-green/10" : "border-destructive/40 bg-destructive/10",
        )}
      >
        <div className="flex items-center gap-3">
          {result.passed ? (
            <Icons.success className="size-6 shrink-0 text-green" />
          ) : (
            <Icons.error className="size-6 shrink-0 text-destructive" />
          )}
          <div>
            <p className="text-sm font-semibold">
              {result.scorePercent}% — {result.passed ? "пройдено" : "не пройдено"}
            </p>
            <p className="text-xs text-muted-foreground">
              Проходной балл: {result.passingScorePercent}% ·{" "}
              {formatShortDateWithTime(result.submittedAt)}
            </p>
          </div>
        </div>
        <Button variant="outline" size="sm" onClick={onRetake}>
          <Icons.refresh className="size-3.5" />
          {retakeLabel ?? "Пройти ещё раз"}
        </Button>
      </div>

      <div className="space-y-5">
        {result.questions.map((questionResult, index) => (
          <QuestionReview
            key={questionResult.questionId}
            index={index}
            questionResult={questionResult}
            question={questionById.get(questionResult.questionId)}
          />
        ))}
      </div>
    </div>
  );
}

function QuestionReview({
  index,
  questionResult,
  question,
}: {
  index: number;
  questionResult: QuizAttemptQuestionResultDto;
  question: QuizQuestionStudentDto | undefined;
}) {
  const isSelfCheck = questionResult.correct === null;

  return (
    <div className="space-y-3">
      <div className="flex items-start gap-2">
        {questionResult.correct === true && (
          <Icons.success className="mt-0.5 size-4 shrink-0 text-green" />
        )}
        {questionResult.correct === false && (
          <Icons.error className="mt-0.5 size-4 shrink-0 text-destructive" />
        )}
        {isSelfCheck && <Icons.help className="mt-0.5 size-4 shrink-0 text-muted-foreground" />}
        <div className="min-w-0 flex-1 text-sm font-medium leading-snug">
          {index + 1}.{" "}
          {question ? <QuizRichText text={question.text} /> : "Вопрос был изменён автором"}
        </div>
        {isSelfCheck && (
          <Badge variant="outline" className="ml-auto shrink-0 text-[11px] text-muted-foreground">
            Самопроверка
          </Badge>
        )}
      </div>

      {questionResult.type !== "OPEN_TEXT" && questionResult.type !== "EXACT_TEXT" && (
        <div className="grid gap-2">
          {questionResult.options.map((option) => {
            const isCorrect = questionResult.correctOptionIds.includes(option.id);
            const isSelected = questionResult.selectedOptionIds.includes(option.id);
            return (
              <div
                key={option.id}
                className={cn(
                  "flex items-start gap-3 rounded-lg border p-3",
                  isCorrect
                    ? "border-green/50 bg-green/10"
                    : isSelected
                      ? "border-destructive/50 bg-destructive/10"
                      : "border-border/60 bg-card/50",
                )}
              >
                {isCorrect ? (
                  <Icons.check className="mt-0.5 size-4 shrink-0 text-green" />
                ) : isSelected ? (
                  <Icons.close className="mt-0.5 size-4 shrink-0 text-destructive" />
                ) : (
                  <span className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
                )}
                <div className="min-w-0 flex-1">
                  <QuizOptionContent text={option.text} />
                  {isSelected && (
                    <span className="ml-1.5 text-xs text-muted-foreground">— ваш выбор</span>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {(questionResult.type === "OPEN_TEXT" || questionResult.type === "EXACT_TEXT") && (
        <div className="grid gap-2">
          <div
            className={cn(
              "rounded-lg border p-3",
              questionResult.type === "EXACT_TEXT"
                ? questionResult.correct
                  ? "border-green/50 bg-green/10"
                  : "border-destructive/50 bg-destructive/10"
                : "border-border/60 bg-card/50",
            )}
          >
            <p className="mb-1 text-xs font-medium text-muted-foreground">Ваш ответ</p>
            <p
              className={cn(
                "whitespace-pre-wrap text-sm leading-snug",
                questionResult.type === "EXACT_TEXT" && "font-mono",
              )}
            >
              {questionResult.textAnswer?.trim() ? questionResult.textAnswer : "—"}
            </p>
          </div>
          {questionResult.referenceAnswer && (
            <div className="rounded-lg border border-primary/30 bg-primary/5 p-3">
              <p className="mb-1 text-xs font-medium text-primary">
                {questionResult.type === "EXACT_TEXT" ? "Правильный ответ" : "Эталонный ответ"}
              </p>
              <p
                className={cn(
                  "whitespace-pre-wrap text-sm leading-snug",
                  questionResult.type === "EXACT_TEXT" && "font-mono",
                )}
              >
                {questionResult.referenceAnswer}
              </p>
            </div>
          )}
        </div>
      )}

      <ExplanationBlock explanation={questionResult.explanation} />
    </div>
  );
}

/**
 * Нейтральный блок «Пояснение» (#561) — рендерится под разбором вопроса, если автор
 * задал пояснение. Переиспользуется в немедленной проверке (`quiz-attempt-form`).
 */
export function ExplanationBlock({ explanation }: { explanation: string | null | undefined }) {
  if (!explanation || explanation.trim().length === 0) return null;
  return (
    <div className="flex items-start gap-2 rounded-lg border border-border/60 bg-muted/40 p-3">
      <Icons.lightbulb className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div className="min-w-0">
        <p className="mb-0.5 text-xs font-medium text-muted-foreground">Пояснение</p>
        <QuizRichText
          text={explanation}
          className="whitespace-pre-wrap text-sm leading-snug text-foreground/90"
        />
      </div>
    </div>
  );
}
