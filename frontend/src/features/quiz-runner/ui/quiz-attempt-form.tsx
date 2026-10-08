"use client";

import {
  QuizOptionContent,
  QuizRichText,
  quizAttemptsApi,
  stableShuffleOptions,
  type CheckQuizQuestionResultDto,
  type QuizStudentDto,
  type SubmitQuizAnswerItem,
} from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Input } from "@/shared/ui/kit/input";
import { RadioGroup, RadioGroupItem } from "@/shared/ui/kit/radio-group";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState, type ReactNode } from "react";
import { toast } from "sonner";
import { ExplanationBlock } from "./quiz-attempt-review";

interface AnswerDraft {
  selectedOptionIds: string[];
  textAnswer: string;
}

interface QuizAttemptFormProps {
  quiz: QuizStudentDto;
  onSubmit: (answers: SubmitQuizAnswerItem[]) => void;
  isPending: boolean;
  /**
   * Принудительная блокировка отправки — анонимный просмотр PUBLIC-квиза
   * (ST-16 #495): вопросы прокликиваются, но сабмит требует авторизации.
   */
  submitDisabled?: boolean;
  /**
   * Показывать ли кнопку «Проверить ответ» под каждым вопросом (#556). Доступна
   * только авторизованным в COURSE-квизе — серверный чек требует Content.VIEW.
   * Анонимный просмотр / level-test проходят с `false`.
   */
  allowCheckAnswers?: boolean;
  /** Слот под кнопкой «Ответить» — CTA «Войти, чтобы сохранить результат». */
  belowSubmit?: ReactNode;
}

const EMPTY_ANSWER: AnswerDraft = { selectedOptionIds: [], textAnswer: "" };

/**
 * Форма прохождения квиза: SINGLE_CHOICE — radio, MULTI_CHOICE — checkbox,
 * OPEN_TEXT — textarea. «Ответить» активна, когда у каждого choice-вопроса
 * выбран хотя бы один вариант; открытый ответ можно оставить пустым. Issue #471.
 *
 * «Проверить ответ» (#556, COURSE-квизы) — необязательная per-вопрос самопроверка:
 * по клику сервер раскрывает правильный ответ ИМЕННО этого вопроса (ключ не уезжает
 * заранее), после чего вопрос фиксируется (ответ менять нельзя). Финальный сабмит
 * грейдит всю попытку как обычно.
 */
export function QuizAttemptForm({
  quiz,
  onSubmit,
  isPending,
  submitDisabled = false,
  allowCheckAnswers = false,
  belowSubmit,
}: QuizAttemptFormProps) {
  const [answers, setAnswers] = useState<Record<string, AnswerDraft>>({});
  const [checked, setChecked] = useState<Record<string, CheckQuizQuestionResultDto>>({});
  const [checkingId, setCheckingId] = useState<string | null>(null);

  const getAnswer = (questionId: string): AnswerDraft => answers[questionId] ?? EMPTY_ANSWER;
  const isLocked = (questionId: string): boolean => checked[questionId] !== undefined;

  const setSingleChoice = (questionId: string, optionId: string) => {
    setAnswers((prev) => ({
      ...prev,
      [questionId]: { ...EMPTY_ANSWER, selectedOptionIds: [optionId] },
    }));
  };

  const toggleMultiChoice = (questionId: string, optionId: string, checkedValue: boolean) => {
    setAnswers((prev) => {
      const current = prev[questionId] ?? EMPTY_ANSWER;
      const selectedOptionIds = checkedValue
        ? [...current.selectedOptionIds, optionId]
        : current.selectedOptionIds.filter((id) => id !== optionId);
      return { ...prev, [questionId]: { ...current, selectedOptionIds } };
    });
  };

  const setTextAnswer = (questionId: string, textAnswer: string) => {
    setAnswers((prev) => ({
      ...prev,
      [questionId]: { ...EMPTY_ANSWER, textAnswer },
    }));
  };

  const checkQuestion = async (questionId: string) => {
    const answer = getAnswer(questionId);
    setCheckingId(questionId);
    try {
      const result = await quizAttemptsApi.checkQuestion(quiz.id, questionId, {
        selectedOptionIds: answer.selectedOptionIds,
        textAnswer: answer.textAnswer.trim().length > 0 ? answer.textAnswer.trim() : null,
      });
      setChecked((prev) => ({ ...prev, [questionId]: result }));
    } catch (error) {
      toast.error(getErrorMessage(error, "Не удалось проверить ответ"));
    } finally {
      setCheckingId(null);
    }
  };

  const canCheck = (questionId: string, type: QuizStudentDto["questions"][number]["type"]): boolean => {
    const answer = getAnswer(questionId);
    if (type === "OPEN_TEXT" || type === "EXACT_TEXT") {
      return answer.textAnswer.trim().length > 0;
    }
    return answer.selectedOptionIds.length > 0;
  };

  const allChoiceAnswered = quiz.questions.every(
    (question) =>
      question.type === "OPEN_TEXT" ||
      question.type === "EXACT_TEXT" ||
      getAnswer(question.id).selectedOptionIds.length > 0,
  );

  const handleSubmit = () => {
    const payload: SubmitQuizAnswerItem[] = quiz.questions.map((question) => {
      const answer = getAnswer(question.id);
      if (question.type === "OPEN_TEXT" || question.type === "EXACT_TEXT") {
        const text = answer.textAnswer.trim();
        return { questionId: question.id, textAnswer: text.length > 0 ? text : null };
      }
      return { questionId: question.id, selectedOptionIds: answer.selectedOptionIds };
    });
    onSubmit(payload);
  };

  return (
    <div className="space-y-6">
      {quiz.questions.map((question, index) => {
        const answer = getAnswer(question.id);
        const result = checked[question.id];
        const locked = isLocked(question.id);
        const disabled = isPending || locked;
        const isCorrectOption = (optionId: string) =>
          result?.correctOptionIds.includes(optionId) ?? false;
        // Стабильный шафл (#561): правильный вариант не должен оседать первым.
        // Детерминирован по id → порядок не прыгает при ре-рендере (выбор ответа,
        // проверка). Грейдинг идёт по optionId, поэтому порядок не влияет.
        const displayOptions = stableShuffleOptions(question.id, question.options);
        return (
          <fieldset key={question.id} className="space-y-3">
            <legend className="text-sm font-medium leading-snug">
              {index + 1}. <QuizRichText text={question.text} />
              {question.type === "MULTI_CHOICE" && (
                <span className="ml-1.5 text-xs font-normal text-muted-foreground">
                  (несколько вариантов)
                </span>
              )}
            </legend>

            {question.type === "SINGLE_CHOICE" && (
              <RadioGroup
                value={answer.selectedOptionIds[0] ?? ""}
                onValueChange={(optionId) => setSingleChoice(question.id, optionId)}
                disabled={disabled}
                className="gap-2"
              >
                {displayOptions.map((option) => {
                  const selected = answer.selectedOptionIds.includes(option.id);
                  return (
                    <label
                      key={option.id}
                      className={cn(
                        "flex cursor-pointer items-start gap-3 rounded-lg border border-border/60 bg-card/50 p-3 transition-colors hover:bg-accent/40",
                        "has-[[data-state=checked]]:border-primary/60 has-[[data-state=checked]]:bg-primary/5",
                        result && isCorrectOption(option.id) && "border-green/60 bg-green/5",
                        result && selected && !isCorrectOption(option.id) &&
                          "border-destructive/60 bg-destructive/5",
                      )}
                    >
                      <RadioGroupItem value={option.id} className="mt-0.5" />
                      <QuizOptionContent text={option.text} />
                    </label>
                  );
                })}
              </RadioGroup>
            )}

            {question.type === "MULTI_CHOICE" && (
              <div className="grid gap-2">
                {displayOptions.map((option) => {
                  const isChecked = answer.selectedOptionIds.includes(option.id);
                  return (
                    <label
                      key={option.id}
                      className={cn(
                        "flex cursor-pointer items-start gap-3 rounded-lg border border-border/60 bg-card/50 p-3 transition-colors hover:bg-accent/40",
                        isChecked && "border-primary/60 bg-primary/5",
                        result && isCorrectOption(option.id) && "border-green/60 bg-green/5",
                        result && isChecked && !isCorrectOption(option.id) &&
                          "border-destructive/60 bg-destructive/5",
                      )}
                    >
                      <Checkbox
                        checked={isChecked}
                        onCheckedChange={(value) =>
                          toggleMultiChoice(question.id, option.id, value === true)
                        }
                        disabled={disabled}
                        className="mt-0.5"
                      />
                      <QuizOptionContent text={option.text} />
                    </label>
                  );
                })}
              </div>
            )}

            {question.type === "OPEN_TEXT" && (
              <Textarea
                value={answer.textAnswer}
                onChange={(event) => setTextAnswer(question.id, event.target.value)}
                disabled={disabled}
                placeholder="Ваш ответ (необязательно — сверитесь с эталоном после отправки)"
                className="min-h-24 bg-card/50"
              />
            )}

            {question.type === "EXACT_TEXT" && (
              <div className="grid gap-1.5">
                <Input
                  value={answer.textAnswer}
                  onChange={(event) => setTextAnswer(question.id, event.target.value)}
                  disabled={disabled}
                  placeholder="Введи точный ответ…"
                  autoComplete="off"
                  spellCheck={false}
                  className="bg-card/50 font-mono"
                />
                <p className="text-xs text-muted-foreground">
                  Регистр, пробелы и знаки препинания не важны.
                </p>
              </div>
            )}

            {allowCheckAnswers && !result && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => checkQuestion(question.id)}
                disabled={isPending || checkingId === question.id || !canCheck(question.id, question.type)}
              >
                {checkingId === question.id ? (
                  <Icons.loading className="size-3.5 animate-spin" />
                ) : (
                  <Icons.check className="size-3.5" />
                )}
                Проверить ответ
              </Button>
            )}

            {result && <QuestionCheckResult result={result} />}
          </fieldset>
        );
      })}

      <div className="space-y-3">
        <div className="flex items-center gap-3">
          <Button
            onClick={handleSubmit}
            disabled={submitDisabled || !allChoiceAnswered || isPending}
          >
            {isPending ? (
              <Icons.loading className="size-4 animate-spin" />
            ) : (
              <Icons.send className="size-4" />
            )}
            Ответить
          </Button>
          {!submitDisabled && !allChoiceAnswered && (
            <p className="text-xs text-muted-foreground">
              Ответьте на все вопросы с вариантами, чтобы отправить
            </p>
          )}
        </div>
        {belowSubmit}
      </div>
    </div>
  );
}

/**
 * Итог per-вопрос проверки (#556): вердикт + эталон для открытых вопросов.
 * Под итогом — нейтральный блок «Пояснение» (#561), если автор его задал.
 */
function QuestionCheckResult({ result }: { result: CheckQuizQuestionResultDto }) {
  return (
    <div className="space-y-2">
      {result.correct === null ? (
        <div className="rounded-lg border border-border/60 bg-card/50 p-3 text-xs">
          <p className="font-medium text-muted-foreground">Сверьтесь с эталоном:</p>
          {result.referenceAnswer ? (
            <QuizOptionContent text={result.referenceAnswer} />
          ) : (
            <p className="text-muted-foreground">Эталон не задан.</p>
          )}
        </div>
      ) : (
        <p
          className={cn(
            "flex items-center gap-1.5 text-xs font-medium",
            result.correct ? "text-green" : "text-destructive",
          )}
        >
          {result.correct ? (
            <Icons.completed className="size-3.5" />
          ) : (
            <Icons.close className="size-3.5" />
          )}
          {result.correct ? "Верно" : "Неверно — правильный вариант выделен зелёным"}
        </p>
      )}
      <ExplanationBlock explanation={result.explanation} />
    </div>
  );
}
