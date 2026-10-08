"use client";

import type {
  LevelTestQuestionDto,
  LevelTestStudentDto,
  SubmitLevelTestAnswerItem,
} from "@/entities/level-test";
import { QuizOptionContent, stableShuffleOptions } from "@/entities/quiz";
import { cn } from "@/shared/lib/css";
import { ProgressBar } from "@/shared/ui/components";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Input } from "@/shared/ui/kit/input";
import { RadioGroup, RadioGroupItem } from "@/shared/ui/kit/radio-group";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState } from "react";
import {
  buildSubmitAnswers,
  countAnswered,
  getAnswerDraft,
  isQuestionAnswered,
  setSingleChoice,
  setTextAnswer,
  toggleMultiChoice,
  type LevelTestAnswersState,
} from "../model/answers";
import { DEVELOPER_LEVEL_BADGE_CLASSES, DEVELOPER_LEVEL_LABELS } from "../model/level-visuals";

interface LevelTestRunnerProps {
  test: LevelTestStudentDto;
  onSubmit: (answers: SubmitLevelTestAnswerItem[]) => void;
  isSubmitting: boolean;
  /** Восстановление черновика после reload (#528). */
  initialAnswers?: LevelTestAnswersState;
  initialIndex?: number;
  /** Персист черновика: дёргается на каждом изменении ответа/перелистывании. */
  onProgress?: (answers: LevelTestAnswersState, index: number) => void;
  /** «Назад» с первого вопроса — выход к описанию теста (черновик сохраняется). */
  onExit?: () => void;
}

/**
 * Прохождение теста: один вопрос на экран, степпер «Вопрос N из M» + чип секции
 * + бейдж сложности. Текст вопроса — markdown (```csharp подсвечивается).
 * Назад/далее, ответы в локальном состоянии; незаполненные ответы разрешены —
 * перед «Завершить тест» предупреждаем счётчиком «Отвечено X из Y». Issue #481.
 */
export function LevelTestRunner({
  test,
  onSubmit,
  isSubmitting,
  initialAnswers,
  initialIndex,
  onProgress,
  onExit,
}: LevelTestRunnerProps) {
  const total = test.questions.length;
  const [answers, setAnswers] = useState<LevelTestAnswersState>(() => initialAnswers ?? {});
  const [index, setIndex] = useState(() =>
    Math.min(Math.max(initialIndex ?? 0, 0), Math.max(total - 1, 0)),
  );

  const updateAnswers = (next: LevelTestAnswersState) => {
    setAnswers(next);
    onProgress?.(next, index);
  };

  const goToIndex = (next: number) => {
    setIndex(next);
    onProgress?.(answers, next);
  };

  const question = test.questions[index];
  const isLast = index === total - 1;
  const answeredCount = countAnswered(test.questions, answers);
  const sectionTitle = question.section
    ? (test.sections.find((section) => section.key === question.section)?.title ?? question.section)
    : null;

  const handleSubmit = () => {
    onSubmit(buildSubmitAnswers(test.questions, answers));
  };

  return (
    <div className="mx-auto w-full max-w-3xl space-y-4">
      {/* Степпер */}
      <div className="space-y-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm font-medium text-muted-foreground">
            Вопрос {index + 1} из {total}
          </p>
          <div className="flex flex-wrap items-center gap-1.5">
            {sectionTitle && (
              <Badge variant="outline" className="font-normal text-muted-foreground">
                {sectionTitle}
              </Badge>
            )}
            {question.difficulty && (
              <Badge
                variant="outline"
                className={cn("font-medium", DEVELOPER_LEVEL_BADGE_CLASSES[question.difficulty])}
              >
                {DEVELOPER_LEVEL_LABELS[question.difficulty]}
              </Badge>
            )}
          </div>
        </div>
        <ProgressBar value={((index + 1) / total) * 100} />

        {/* Карта вопросов (#528): мгновенный переход + видно неотвеченные. */}
        <div className="flex flex-wrap gap-1.5 pt-1">
          {test.questions.map((item, itemIndex) => {
            const answered = isQuestionAnswered(item, answers);
            const current = itemIndex === index;
            return (
              <button
                key={item.id}
                type="button"
                disabled={isSubmitting}
                onClick={() => goToIndex(itemIndex)}
                aria-label={`Вопрос ${itemIndex + 1}${answered ? " — отвечен" : " — без ответа"}`}
                aria-current={current ? "step" : undefined}
                className={cn(
                  "size-8 rounded-md border text-xs tabular-nums transition-colors sm:size-7",
                  current
                    ? "border-primary bg-primary/15 font-medium text-primary"
                    : answered
                      ? "border-primary/35 bg-primary/10 text-foreground/75"
                      : "border-border/60 bg-card/50 text-muted-foreground hover:bg-accent/40",
                )}
              >
                {itemIndex + 1}
              </button>
            );
          })}
        </div>
      </div>

      {/* Вопрос */}
      <div className="rounded-xl border border-border/60 bg-card p-4 sm:p-6">
        {/* Код — горизонтальный скролл вместо переноса строк (wrapLongLines
            ставит inline pre-wrap, перебиваем !important-утилитой). */}
        <MarkdownContent
          variant="compact"
          disableLinks
          className="[&>*:first-child]:mt-0 [&>*:last-child]:mb-0 [&_pre]:overflow-x-auto [&_pre_code]:!whitespace-pre"
        >
          {question.text}
        </MarkdownContent>

        <div className="mt-5">
          <QuestionInput
            question={question}
            answers={answers}
            disabled={isSubmitting}
            onChange={updateAnswers}
          />
        </div>
      </div>

      {/* Навигация: кнопки всегда в одну линию, счётчик — отдельной строкой под ними */}
      <div className="flex flex-col gap-1.5">
        <div className="flex items-center justify-between gap-3">
          <Button
            type="button"
            variant="outline"
            disabled={isSubmitting || (index === 0 && !onExit)}
            onClick={() => (index === 0 ? onExit?.() : goToIndex(Math.max(0, index - 1)))}
          >
            <Icons.chevronLeft className="size-4" />
            {index === 0 ? "К описанию" : "Назад"}
          </Button>

          {isLast ? (
            <Button type="button" disabled={isSubmitting} onClick={handleSubmit}>
              {isSubmitting ? (
                <Icons.loading className="size-4 animate-spin" />
              ) : (
                <Icons.send className="size-4" />
              )}
              Завершить тест
            </Button>
          ) : (
            <Button
              type="button"
              disabled={isSubmitting}
              onClick={() => goToIndex(Math.min(total - 1, index + 1))}
            >
              Далее
              <Icons.chevronRight className="size-4" />
            </Button>
          )}
        </div>
        {isLast && (
          <p
            className={cn(
              "self-end text-right text-xs",
              answeredCount < total
                ? "text-amber-600 dark:text-amber-400"
                : "text-muted-foreground",
            )}
          >
            Отвечено {answeredCount} из {total}
            {answeredCount < total && " — без ответа засчитается как неверный"}
          </p>
        )}
      </div>
    </div>
  );
}

interface QuestionInputProps {
  question: LevelTestQuestionDto;
  answers: LevelTestAnswersState;
  disabled: boolean;
  onChange: (next: LevelTestAnswersState) => void;
}

function QuestionInput({ question, answers, disabled, onChange }: QuestionInputProps) {
  const draft = getAnswerDraft(answers, question.id);
  // Стабильный шафл (#528): правильный вариант не должен оседать на первой позиции.
  const displayOptions = stableShuffleOptions(question.id, question.options);

  if (question.type === "SINGLE_CHOICE") {
    return (
      <RadioGroup
        value={draft.selectedOptionIds[0] ?? ""}
        onValueChange={(optionId) => onChange(setSingleChoice(answers, question.id, optionId))}
        disabled={disabled}
        className="gap-2"
      >
        {displayOptions.map((option) => (
          <label
            key={option.id}
            className={cn(
              "flex min-w-0 cursor-pointer items-start gap-3 rounded-lg border border-border/60 bg-card/50 p-3 transition-colors hover:bg-accent/40",
              "has-[[data-state=checked]]:border-primary/60 has-[[data-state=checked]]:bg-primary/5",
            )}
          >
            <RadioGroupItem value={option.id} className="mt-0.5" />
            <QuizOptionContent text={option.text} />
          </label>
        ))}
      </RadioGroup>
    );
  }

  if (question.type === "MULTI_CHOICE") {
    return (
      <div className="grid gap-2">
        <p className="text-xs text-muted-foreground">
          Несколько вариантов ответа — засчитывается только полностью верная комбинация
        </p>
        {displayOptions.map((option) => {
          const checked = draft.selectedOptionIds.includes(option.id);
          return (
            <label
              key={option.id}
              className={cn(
                "flex min-w-0 cursor-pointer items-start gap-3 rounded-lg border border-border/60 bg-card/50 p-3 transition-colors hover:bg-accent/40",
                checked && "border-primary/60 bg-primary/5",
              )}
            >
              <Checkbox
                checked={checked}
                onCheckedChange={(value) =>
                  onChange(toggleMultiChoice(answers, question.id, option.id, value === true))
                }
                disabled={disabled}
                className="mt-0.5"
              />
              <QuizOptionContent text={option.text} />
            </label>
          );
        })}
      </div>
    );
  }

  if (question.type === "EXACT_TEXT") {
    return (
      <div className="grid gap-1.5">
        <Input
          value={draft.textAnswer}
          onChange={(event) => onChange(setTextAnswer(answers, question.id, event.target.value))}
          disabled={disabled}
          placeholder="Введи точный вывод программы…"
          autoComplete="off"
          spellCheck={false}
          className="bg-card/50 font-mono"
        />
        <p className="text-xs text-muted-foreground">
          Регистр, пробелы и знаки препинания не важны. Несколько значений вводи через пробел или
          запятую.
        </p>
      </div>
    );
  }

  return (
    <Textarea
      value={draft.textAnswer}
      onChange={(event) => onChange(setTextAnswer(answers, question.id, event.target.value))}
      disabled={disabled}
      placeholder="Кратко, тезисами — 3–6 ключевых пунктов…"
      className="min-h-28 bg-card/50"
    />
  );
}
