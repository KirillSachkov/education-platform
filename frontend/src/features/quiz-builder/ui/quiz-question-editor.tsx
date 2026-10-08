"use client";

import {
  QUIZ_DIFFICULTY_LEVELS,
  QUIZ_EXPLANATION_MAX_LENGTH,
  QUIZ_MAX_OPTIONS,
  QUIZ_MIN_OPTIONS,
  QUIZ_OPTION_TEXT_MAX_LENGTH,
  QUIZ_QUESTION_TEXT_MAX_LENGTH,
  QUIZ_REFERENCE_ANSWER_MAX_LENGTH,
  type QuizDifficultyLevel,
  type QuizQuestionType,
} from "@/entities/quiz";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { RadioGroup, RadioGroupItem } from "@/shared/ui/kit/radio-group";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Textarea } from "@/shared/ui/kit/textarea";
import { createQuizOptionDraft, type QuizQuestionDraft } from "../model/schemas";

const QUESTION_TYPE_LABELS: Record<QuizQuestionType, string> = {
  SINGLE_CHOICE: "Один правильный ответ",
  MULTI_CHOICE: "Несколько правильных",
  OPEN_TEXT: "Открытый ответ",
  EXACT_TEXT: "Точный ответ (ввод)",
};

const DIFFICULTY_LABELS: Record<QuizDifficultyLevel, string> = {
  JUNIOR: "Junior",
  MIDDLE: "Middle",
  SENIOR: "Senior",
};

/**
 * Цвета зеркалят `features/level-test-runner/model/level-visuals.ts` (runner
 * показывает difficulty-бейджи теми же цветами). Cross-feature импорт запрещён
 * FSD — маленькая мапа продублирована сознательно.
 */
const DIFFICULTY_BADGE_CLASSES: Record<QuizDifficultyLevel, string> = {
  JUNIOR: "border-emerald-500/40 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
  MIDDLE: "border-amber-500/40 bg-amber-500/10 text-amber-600 dark:text-amber-400",
  SENIOR: "border-violet-500/40 bg-violet-500/10 text-violet-600 dark:text-violet-400",
};

/** Сентинел «не выбрано» для Radix Select (пустые value у SelectItem запрещены). */
const NONE_VALUE = "__none__";

/** Level-test режим редактора вопроса: список секций конфига для селекта «Секция». */
export interface QuizQuestionLevelTestProps {
  sections: { key: string; title: string }[];
}

interface QuizQuestionEditorProps {
  question: QuizQuestionDraft;
  index: number;
  totalCount: number;
  /** Ошибки валидации всей формы, ключи вида `questions.{index}.text`. */
  errors: Record<string, string>;
  onChange: (question: QuizQuestionDraft) => void;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
  disabled: boolean;
  /**
   * Level-test поля (#487): селекты «Секция» (из секций конфига) и «Сложность».
   * Не передан (material-билдер) — селекты не рендерятся, section/difficulty
   * остаются `null`.
   */
  levelTest?: QuizQuestionLevelTestProps;
}

/**
 * Редактор одного вопроса: тип (3 варианта), текст, для choice — варианты с
 * отметкой правильных (radio/checkbox по типу), для OPEN_TEXT — эталонный ответ.
 * Reorder — простые стрелки вверх/вниз (без drag, как reorder-arrows в
 * course-builder). Issue #471.
 */
export function QuizQuestionEditor({
  question,
  index,
  totalCount,
  errors,
  onChange,
  onRemove,
  onMoveUp,
  onMoveDown,
  disabled,
  levelTest,
}: QuizQuestionEditorProps) {
  const errorKey = (suffix: string) => errors[`questions.${index}.${suffix}`];
  const isChoice = question.type !== "OPEN_TEXT" && question.type !== "EXACT_TEXT";

  const handleTypeChange = (type: QuizQuestionType) => {
    if (type === question.type) return;
    if (type === "OPEN_TEXT" || type === "EXACT_TEXT") {
      onChange({ ...question, type, options: [], correctOptionIds: [] });
      return;
    }
    const options =
      question.options.length >= QUIZ_MIN_OPTIONS
        ? question.options
        : [
            ...question.options,
            ...Array.from(
              { length: QUIZ_MIN_OPTIONS - question.options.length },
              createQuizOptionDraft,
            ),
          ];
    // MULTI → SINGLE: оставляем максимум один правильный.
    const correctOptionIds =
      type === "SINGLE_CHOICE" ? question.correctOptionIds.slice(0, 1) : question.correctOptionIds;
    onChange({ ...question, type, options, correctOptionIds, referenceAnswer: "" });
  };

  const handleOptionTextChange = (optionId: string, text: string) => {
    onChange({
      ...question,
      options: question.options.map((option) =>
        option.id === optionId ? { ...option, text } : option,
      ),
    });
  };

  const handleAddOption = () => {
    onChange({ ...question, options: [...question.options, createQuizOptionDraft()] });
  };

  const handleRemoveOption = (optionId: string) => {
    onChange({
      ...question,
      options: question.options.filter((option) => option.id !== optionId),
      correctOptionIds: question.correctOptionIds.filter((id) => id !== optionId),
    });
  };

  const handleSingleCorrectChange = (optionId: string) => {
    onChange({ ...question, correctOptionIds: [optionId] });
  };

  const handleMultiCorrectToggle = (optionId: string, checked: boolean) => {
    onChange({
      ...question,
      correctOptionIds: checked
        ? [...question.correctOptionIds, optionId]
        : question.correctOptionIds.filter((id) => id !== optionId),
    });
  };

  return (
    <div className="space-y-3 rounded-xl border border-border/60 bg-card/50 p-4">
      <div className="flex items-center gap-2">
        <span className="text-sm font-semibold text-muted-foreground">Вопрос {index + 1}</span>
        <div className="ml-auto flex items-center gap-1">
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-7"
            onClick={onMoveUp}
            disabled={disabled || index === 0}
            aria-label="Переместить вверх"
          >
            <Icons.chevronUp className="size-4" />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-7"
            onClick={onMoveDown}
            disabled={disabled || index === totalCount - 1}
            aria-label="Переместить вниз"
          >
            <Icons.chevronDown className="size-4" />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-7 text-muted-foreground hover:text-destructive"
            onClick={onRemove}
            disabled={disabled}
            aria-label="Удалить вопрос"
          >
            <Icons.delete className="size-4" />
          </Button>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px]">
        <div className="space-y-1.5">
          <Textarea
            value={question.text}
            onChange={(event) => onChange({ ...question, text: event.target.value })}
            maxLength={QUIZ_QUESTION_TEXT_MAX_LENGTH}
            placeholder="Текст вопроса"
            disabled={disabled}
            className="min-h-16 bg-background/60"
          />
          {errorKey("text") && <p className="text-xs text-destructive">{errorKey("text")}</p>}
        </div>
        <Select
          value={question.type}
          onValueChange={(value) => handleTypeChange(value as QuizQuestionType)}
          disabled={disabled}
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {(Object.keys(QUESTION_TYPE_LABELS) as QuizQuestionType[]).map((type) => (
              <SelectItem key={type} value={type}>
                {QUESTION_TYPE_LABELS[type]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {levelTest && (
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor={`question-section-${question.id}`}>Секция</Label>
            <Select
              value={question.section ?? NONE_VALUE}
              onValueChange={(value) =>
                onChange({ ...question, section: value === NONE_VALUE ? null : value })
              }
              disabled={disabled}
            >
              <SelectTrigger id={`question-section-${question.id}`} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE_VALUE}>Без секции</SelectItem>
                {levelTest.sections.map((section) => (
                  <SelectItem key={section.key} value={section.key}>
                    {section.title}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor={`question-difficulty-${question.id}`}>Сложность</Label>
            <Select
              value={question.difficulty ?? NONE_VALUE}
              onValueChange={(value) =>
                onChange({
                  ...question,
                  difficulty: value === NONE_VALUE ? null : (value as QuizDifficultyLevel),
                })
              }
              disabled={disabled}
            >
              <SelectTrigger id={`question-difficulty-${question.id}`} className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE_VALUE}>Без сложности</SelectItem>
                {QUIZ_DIFFICULTY_LEVELS.map((level) => (
                  <SelectItem key={level} value={level}>
                    <span
                      className={cn(
                        "rounded-md border px-1.5 py-0.5 text-xs font-medium",
                        DIFFICULTY_BADGE_CLASSES[level],
                      )}
                    >
                      {DIFFICULTY_LABELS[level]}
                    </span>
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>
      )}

      {isChoice && (
        <div className="space-y-2">
          <p className="text-xs text-muted-foreground">
            {question.type === "SINGLE_CHOICE"
              ? "Отметьте правильный вариант"
              : "Отметьте правильные варианты"}
          </p>

          {question.type === "SINGLE_CHOICE" ? (
            <RadioGroup
              value={question.correctOptionIds[0] ?? ""}
              onValueChange={handleSingleCorrectChange}
              disabled={disabled}
              className="gap-2"
            >
              {question.options.map((option, optionIndex) => (
                <OptionRow
                  key={option.id}
                  marker={<RadioGroupItem value={option.id} aria-label="Правильный вариант" />}
                  text={option.text}
                  onTextChange={(text) => handleOptionTextChange(option.id, text)}
                  onRemove={() => handleRemoveOption(option.id)}
                  canRemove={question.options.length > QUIZ_MIN_OPTIONS}
                  disabled={disabled}
                  error={errorKey(`options.${optionIndex}.text`)}
                />
              ))}
            </RadioGroup>
          ) : (
            <div className="grid gap-2">
              {question.options.map((option, optionIndex) => (
                <OptionRow
                  key={option.id}
                  marker={
                    <Checkbox
                      checked={question.correctOptionIds.includes(option.id)}
                      onCheckedChange={(value) =>
                        handleMultiCorrectToggle(option.id, value === true)
                      }
                      disabled={disabled}
                      aria-label="Правильный вариант"
                    />
                  }
                  text={option.text}
                  onTextChange={(text) => handleOptionTextChange(option.id, text)}
                  onRemove={() => handleRemoveOption(option.id)}
                  canRemove={question.options.length > QUIZ_MIN_OPTIONS}
                  disabled={disabled}
                  error={errorKey(`options.${optionIndex}.text`)}
                />
              ))}
            </div>
          )}

          {(errorKey("options") || errorKey("correctOptionIds")) && (
            <p className="text-xs text-destructive">
              {errorKey("options") ?? errorKey("correctOptionIds")}
            </p>
          )}

          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={handleAddOption}
            disabled={disabled || question.options.length >= QUIZ_MAX_OPTIONS}
          >
            <Icons.add className="size-3.5" />
            Добавить вариант
          </Button>
        </div>
      )}

      {question.type === "EXACT_TEXT" && (
        <div className="space-y-1.5">
          <Input
            value={question.referenceAnswer}
            onChange={(event) => onChange({ ...question, referenceAnswer: event.target.value })}
            maxLength={QUIZ_REFERENCE_ANSWER_MAX_LENGTH}
            placeholder="Эталонный точный ответ (например, вывод программы)"
            disabled={disabled}
            className="bg-background/60 font-mono"
          />
          <p className="text-xs text-muted-foreground">
            Сверка детерминированная: регистр, пробелы и пунктуация ввода не учитываются.
          </p>
          {errorKey("referenceAnswer") && (
            <p className="text-xs text-destructive">{errorKey("referenceAnswer")}</p>
          )}
        </div>
      )}

      {question.type === "OPEN_TEXT" && (
        <div className="space-y-1.5">
          <Textarea
            value={question.referenceAnswer}
            onChange={(event) => onChange({ ...question, referenceAnswer: event.target.value })}
            maxLength={QUIZ_REFERENCE_ANSWER_MAX_LENGTH}
            placeholder="Эталонный ответ (студент увидит его после отправки — самопроверка)"
            disabled={disabled}
            className="min-h-20 bg-background/60"
          />
          {levelTest && (
            <p className="text-xs text-muted-foreground">
              Эталон скармливается AI-грейдеру. Формат: «Эталон: … Критерии оценки: 1) … (~N из
              100)».
            </p>
          )}
          {errorKey("referenceAnswer") && (
            <p className="text-xs text-destructive">{errorKey("referenceAnswer")}</p>
          )}
        </div>
      )}

      <div className="space-y-1.5">
        <Label htmlFor={`question-explanation-${question.id}`}>Пояснение (необязательно)</Label>
        <Textarea
          id={`question-explanation-${question.id}`}
          value={question.explanation}
          onChange={(event) => onChange({ ...question, explanation: event.target.value })}
          maxLength={QUIZ_EXPLANATION_MAX_LENGTH}
          placeholder="Почему этот ответ верный — студент увидит пояснение в разборе после ответа"
          disabled={disabled}
          className="min-h-16 bg-background/60"
        />
        {errorKey("explanation") && (
          <p className="text-xs text-destructive">{errorKey("explanation")}</p>
        )}
      </div>
    </div>
  );
}

function OptionRow({
  marker,
  text,
  onTextChange,
  onRemove,
  canRemove,
  disabled,
  error,
}: {
  marker: React.ReactNode;
  text: string;
  onTextChange: (text: string) => void;
  onRemove: () => void;
  canRemove: boolean;
  disabled: boolean;
  error?: string;
}) {
  return (
    <div className="space-y-1">
      <div className="flex items-center gap-2">
        {marker}
        <Input
          value={text}
          onChange={(event) => onTextChange(event.target.value)}
          maxLength={QUIZ_OPTION_TEXT_MAX_LENGTH}
          placeholder="Текст варианта"
          disabled={disabled}
          className="h-9 bg-background/60"
        />
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-7 shrink-0 text-muted-foreground hover:text-destructive"
          onClick={onRemove}
          disabled={disabled || !canRemove}
          aria-label="Удалить вариант"
        >
          <Icons.close className="size-3.5" />
        </Button>
      </div>
      {error && <p className="pl-6 text-xs text-destructive">{error}</p>}
    </div>
  );
}
