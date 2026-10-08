"use client";

import {
  isAiGradingPending,
  type LevelTestAttemptResultDto,
  type LevelTestQuestionResultDto,
} from "@/entities/level-test";
import { cn } from "@/shared/lib/css";
import { ProgressBar } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { DEVELOPER_LEVEL_LABELS } from "../model/level-visuals";
import { resolveWeakestSectionTitles } from "../model/result-summary";
import { LevelTestRecommendationCard } from "./level-test-recommendation-card";
import { LevelTestScoreHeader } from "./level-test-score-header";

/** Очки могут быть дробными после AI-грейдинга (score% × max) — обрезаем хвост. */
function formatPoints(value: number): string {
  return String(Number(value.toFixed(1)));
}

interface LevelTestResultFullProps {
  result: LevelTestAttemptResultDto;
}

/**
 * Полный разбор попытки: общий % + уровень (единственный цветной бейдж — в
 * шапке, остальное сдержанно текстом/primary-барами, #528), слабые места,
 * рекомендация курса и per-вопрос детализация с оценкой развёрнутых ответов
 * (skeleton + поллинг, пока грейдинг идёт). Issue #481.
 */
export function LevelTestResultFull({ result }: LevelTestResultFullProps) {
  const aiPending = isAiGradingPending(result.aiGradingStatus);
  const weakestTitles = resolveWeakestSectionTitles(result.sections, result.weakestSectionKeys);

  return (
    <div className="mx-auto w-full max-w-2xl space-y-8 py-10">
      <LevelTestScoreHeader overallPercent={result.overallPercent} level={result.level} />

      {aiPending && (
        <p className="flex items-center justify-center gap-2 text-sm text-muted-foreground">
          <Icons.loading className="size-4 animate-spin text-primary" aria-hidden />
          Проверяем развёрнутые ответы — проценты ещё уточняются…
        </p>
      )}
      {result.aiGradingStatus === "FAILED" && (
        <p className="mx-auto flex max-w-md items-start gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-700 dark:text-amber-400">
          <Icons.warning className="mt-0.5 size-4 shrink-0" aria-hidden />
          Развёрнутые ответы оценим позже — разбор по остальным вопросам уже готов.
        </p>
      )}

      {/* Секции */}
      {result.sections.length > 0 && (
        <section className="space-y-4">
          <h2 className="text-lg font-semibold">Разбор по темам</h2>
          <div className="space-y-4 rounded-xl border border-border/60 bg-card p-5">
            {result.sections.map((section) => (
              <div key={section.key} className="space-y-1.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-baseline gap-2">
                    <p className="text-sm font-medium">{section.title}</p>
                    {/* Сдержанно (#528): уровень секции — текстом, единственный
                        цветной акцент страницы — главный бейдж в шапке. */}
                    <span className="text-xs text-muted-foreground">
                      {DEVELOPER_LEVEL_LABELS[section.level]}
                    </span>
                  </div>
                  <p className="text-sm tabular-nums text-muted-foreground">
                    {section.percent}%
                    <span className="ml-1.5 text-xs">
                      ({formatPoints(section.earnedPoints)}/{formatPoints(section.maxPoints)} б.)
                    </span>
                  </p>
                </div>
                <ProgressBar
                  value={section.percent}
                  className="[&_[data-slot=progress-indicator]]:bg-primary/70"
                />
              </div>
            ))}
          </div>
          {weakestTitles.length > 0 && (
            <p className="text-sm text-muted-foreground">
              <span className="font-medium text-foreground">Слабые места:</span>{" "}
              {weakestTitles.join(", ")}
            </p>
          )}
        </section>
      )}

      {/* Рекомендация */}
      <LevelTestRecommendationCard
        testId={result.quizId}
        recommendedCourseId={result.recommendedCourseId}
        weakestTitles={weakestTitles}
      />

      {/* Вопросы */}
      <section className="space-y-4">
        <h2 className="text-lg font-semibold">Ответы по вопросам</h2>
        <ol className="space-y-2">
          {result.questions.map((question, index) => (
            <QuestionResultRow
              key={question.questionId}
              question={question}
              index={index}
              sectionTitle={resolveSectionTitle(result, question)}
              aiPending={aiPending}
            />
          ))}
        </ol>
      </section>
    </div>
  );
}

function resolveSectionTitle(
  result: LevelTestAttemptResultDto,
  question: LevelTestQuestionResultDto,
): string | null {
  if (!question.section) return null;
  return (
    result.sections.find((section) => section.key === question.section)?.title ?? question.section
  );
}

interface QuestionResultRowProps {
  question: LevelTestQuestionResultDto;
  index: number;
  sectionTitle: string | null;
  aiPending: boolean;
}

function QuestionResultRow({ question, index, sectionTitle, aiPending }: QuestionResultRowProps) {
  return (
    <li className="rounded-lg border border-border/60 bg-card/50 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <QuestionOutcomeIcon question={question} aiPending={aiPending} />
          <p className="text-sm font-medium">Вопрос {index + 1}</p>
          {sectionTitle && <span className="text-xs text-muted-foreground">{sectionTitle}</span>}
          {question.difficulty && (
            <span className="text-xs text-muted-foreground/70">
              {DEVELOPER_LEVEL_LABELS[question.difficulty]}
            </span>
          )}
        </div>
        <p className="text-xs tabular-nums text-muted-foreground">
          {formatPoints(question.earnedPoints)}/{formatPoints(question.maxPoints)} б.
        </p>
      </div>

      {question.type === "OPEN_TEXT" && (
        <OpenTextAiOutcome question={question} aiPending={aiPending} />
      )}

      <QuestionAnswerBreakdown question={question} />
    </li>
  );
}

/**
 * Разбор правильных ответов (#561): подсветка вариантов для choice-вопросов,
 * ответ студента + эталон для EXACT/OPEN, плюс пояснение. Рендерится только при
 * наличии данных — старые попытки до деплоя отдают пустой `options`/`referenceAnswer`.
 * Визуальный паттерн зеркалит курсовой `quiz-attempt-review`; cross-feature импорт
 * запрещён FSD, разметка продублирована сознательно.
 */
function QuestionAnswerBreakdown({ question }: { question: LevelTestQuestionResultDto }) {
  const isChoice = question.type === "SINGLE_CHOICE" || question.type === "MULTI_CHOICE";
  const isText = question.type === "EXACT_TEXT" || question.type === "OPEN_TEXT";
  const hasChoiceData = isChoice && question.options.length > 0;
  const hasTextData = isText && (!!question.textAnswer?.trim() || !!question.referenceAnswer);
  const hasExplanation = !!question.explanation?.trim();

  if (!hasChoiceData && !hasTextData && !hasExplanation) return null;

  return (
    <div className="mt-3 space-y-2">
      {hasChoiceData && (
        <div className="grid gap-2">
          {question.options.map((option) => {
            const isCorrect = question.correctOptionIds.includes(option.id);
            const isSelected = question.selectedOptionIds.includes(option.id);
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
                <span className="text-sm leading-snug">
                  {option.text}
                  {isSelected && (
                    <span className="ml-1.5 text-xs text-muted-foreground">— ваш выбор</span>
                  )}
                </span>
              </div>
            );
          })}
        </div>
      )}

      {hasTextData && (
        <div className="grid gap-2">
          <div className="rounded-lg border border-border/60 bg-card/50 p-3">
            <p className="mb-1 text-xs font-medium text-muted-foreground">Ваш ответ</p>
            <p
              className={cn(
                "whitespace-pre-wrap text-sm leading-snug",
                question.type === "EXACT_TEXT" && "font-mono",
              )}
            >
              {question.textAnswer?.trim() ? question.textAnswer : "—"}
            </p>
          </div>
          {question.referenceAnswer && (
            <div className="rounded-lg border border-primary/30 bg-primary/5 p-3">
              <p className="mb-1 text-xs font-medium text-primary">
                {question.type === "EXACT_TEXT" ? "Правильный ответ" : "Эталонный ответ"}
              </p>
              <p
                className={cn(
                  "whitespace-pre-wrap text-sm leading-snug",
                  question.type === "EXACT_TEXT" && "font-mono",
                )}
              >
                {question.referenceAnswer}
              </p>
            </div>
          )}
        </div>
      )}

      {hasExplanation && (
        <div className="flex items-start gap-2 rounded-lg border border-border/60 bg-muted/40 p-3">
          <Icons.lightbulb
            className="mt-0.5 size-4 shrink-0 text-muted-foreground"
            aria-hidden="true"
          />
          <div className="min-w-0">
            <p className="mb-0.5 text-xs font-medium text-muted-foreground">Пояснение</p>
            <p className="whitespace-pre-wrap text-sm leading-snug">{question.explanation}</p>
          </div>
        </div>
      )}
    </div>
  );
}

function QuestionOutcomeIcon({
  question,
  aiPending,
}: {
  question: LevelTestQuestionResultDto;
  aiPending: boolean;
}) {
  if (question.isCorrect === true) {
    return <Icons.check className="size-4 text-emerald-500" aria-label="Верно" />;
  }
  if (question.isCorrect === false) {
    return <Icons.close className="size-4 text-red-500" aria-label="Неверно" />;
  }
  if (question.pendingAi && aiPending) {
    return (
      <Icons.loading
        className="size-4 animate-spin text-muted-foreground"
        aria-label="Проверяется"
      />
    );
  }
  return <Icons.ai className="size-4 text-muted-foreground" aria-label="Развёрнутый ответ" />;
}

function OpenTextAiOutcome({
  question,
  aiPending,
}: {
  question: LevelTestQuestionResultDto;
  aiPending: boolean;
}) {
  if (question.pendingAi && aiPending) {
    return (
      <div className="mt-2 space-y-1.5">
        <p className="text-xs text-muted-foreground">Развёрнутый ответ проверяется…</p>
        <Skeleton className="h-3 w-3/4" />
        <Skeleton className="h-3 w-1/2" />
      </div>
    );
  }

  if (question.aiScore !== null) {
    return (
      <div className="mt-2 space-y-1">
        <p className="text-xs font-medium">
          Оценка: <span className="tabular-nums">{question.aiScore}/100</span>
        </p>
        {question.aiFeedback && (
          <p className={cn("whitespace-pre-wrap text-xs leading-relaxed text-muted-foreground")}>
            {question.aiFeedback}
          </p>
        )}
      </div>
    );
  }

  if (question.pendingAi) {
    // Глобальный статус FAILED — ответ есть, но оценки пока нет
    return <p className="mt-2 text-xs text-muted-foreground">Оценим позже</p>;
  }

  return <p className="mt-2 text-xs text-muted-foreground">Без ответа</p>;
}
