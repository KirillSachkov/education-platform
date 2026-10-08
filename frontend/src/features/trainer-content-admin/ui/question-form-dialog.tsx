"use client";

import type {
  TrainerQuestionAdmin,
  TrainerQuestionInput,
  TrainerQuestionOptionInput,
  TrainerQuestionType,
} from "@/entities/trainer-question-admin";
import { TRAINER_QUESTION_DIFFICULTIES } from "@/entities/trainer-question-admin";
import { QUIZ_QUESTION_TYPES } from "@/entities/quiz";
import { TRAINER_DIFFICULTY_VISUALS } from "@/shared/config/trainer";
import { FormDialog } from "@/shared/ui/components/form-dialog";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState } from "react";
import { toast } from "sonner";
import {
  useCreateTrainerQuestion,
  useUpdateTrainerQuestion,
} from "../model/use-question-mutations";

const TYPE_LABELS: Record<TrainerQuestionType, string> = {
  SINGLE_CHOICE: "Один из вариантов",
  MULTI_CHOICE: "Несколько из вариантов",
  EXACT_TEXT: "Точный текстовый ответ",
  OPEN_TEXT: "Развёрнутый ответ (AI-проверка)",
};

const NO_DIFFICULTY = "__none__";

function isChoiceType(type: TrainerQuestionType): boolean {
  return type === "SINGLE_CHOICE" || type === "MULTI_CHOICE";
}

function isReferenceType(type: TrainerQuestionType): boolean {
  return type === "EXACT_TEXT" || type === "OPEN_TEXT";
}

interface OptionDraft extends TrainerQuestionOptionInput {
  /** Локальный ключ строки (для стабильного React key при add/remove). */
  key: string;
}

function newOption(): OptionDraft {
  return { key: crypto.randomUUID(), text: "", isCorrect: false };
}

function initialOptions(question: TrainerQuestionAdmin | null): OptionDraft[] {
  if (question && question.options.length > 0) {
    return question.options.map((o) => ({
      key: o.id,
      text: o.text,
      isCorrect: o.isCorrect,
    }));
  }
  return [newOption(), newOption()];
}

interface QuestionFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  bankId: string;
  topicId: string;
  /** null → создание нового вопроса; иначе редактирование. */
  question: TrainerQuestionAdmin | null;
}

/**
 * Диалог создания/редактирования вопроса собственного банка тренажёра (#623).
 * Стем + тип + варианты (для choice) / эталон (для exact/open) + разбор +
 * сложность + секция. Полная замена при сохранении (PUT-семантика бэка).
 * Mobile-first, touch-цели ≥44px.
 */
export function QuestionFormDialog({
  open,
  onOpenChange,
  bankId,
  topicId,
  question,
}: QuestionFormDialogProps) {
  // key на диалоге (см. вызывающую панель) гарантирует ремоунт под каждый вопрос,
  // поэтому начальный state из props безопасен — без useEffect-синхронизации.
  const [stem, setStem] = useState(question?.stem ?? "");
  const [type, setType] = useState<TrainerQuestionType>(question?.type ?? "SINGLE_CHOICE");
  const [options, setOptions] = useState<OptionDraft[]>(() => initialOptions(question));
  const [referenceAnswer, setReferenceAnswer] = useState(question?.referenceAnswer ?? "");
  const [explanation, setExplanation] = useState(question?.explanation ?? "");
  const [difficulty, setDifficulty] = useState<string>(question?.difficulty ?? NO_DIFFICULTY);
  const [section, setSection] = useState(question?.section ?? "");

  const createMutation = useCreateTrainerQuestion();
  const updateMutation = useUpdateTrainerQuestion();
  const isPending = createMutation.isPending || updateMutation.isPending;

  const addOption = () => setOptions((prev) => [...prev, newOption()]);

  const removeOption = (key: string) =>
    setOptions((prev) => (prev.length <= 2 ? prev : prev.filter((o) => o.key !== key)));

  const updateOptionText = (key: string, text: string) =>
    setOptions((prev) => prev.map((o) => (o.key === key ? { ...o, text } : o)));

  const toggleOptionCorrect = (key: string, isCorrect: boolean) =>
    setOptions((prev) =>
      prev.map((o) => {
        if (o.key !== key) {
          // SINGLE_CHOICE — взаимоисключающий: при выборе одного снимаем остальные.
          return type === "SINGLE_CHOICE" && isCorrect ? { ...o, isCorrect: false } : o;
        }
        return { ...o, isCorrect };
      }),
    );

  const validate = (): TrainerQuestionInput | null => {
    const trimmedStem = stem.trim();
    if (trimmedStem.length === 0) {
      toast.error("Введите текст вопроса");
      return null;
    }

    if (isChoiceType(type)) {
      const cleaned = options
        .map((o) => ({ text: o.text.trim(), isCorrect: o.isCorrect }))
        .filter((o) => o.text.length > 0);
      if (cleaned.length < 2) {
        toast.error("Нужно минимум два варианта ответа");
        return null;
      }
      const correctCount = cleaned.filter((o) => o.isCorrect).length;
      if (type === "SINGLE_CHOICE" && correctCount !== 1) {
        toast.error("Отметьте ровно один правильный вариант");
        return null;
      }
      if (type === "MULTI_CHOICE" && correctCount < 1) {
        toast.error("Отметьте хотя бы один правильный вариант");
        return null;
      }
      return {
        stem: trimmedStem,
        type,
        explanation: explanation.trim() === "" ? null : explanation.trim(),
        difficulty: difficulty === NO_DIFFICULTY ? null : difficulty,
        section: section.trim() === "" ? null : section.trim(),
        options: cleaned,
      };
    }

    // EXACT_TEXT / OPEN_TEXT
    const trimmedReference = referenceAnswer.trim();
    if (type === "EXACT_TEXT" && trimmedReference.length === 0) {
      toast.error("Для точного ответа нужен эталон");
      return null;
    }
    return {
      stem: trimmedStem,
      type,
      referenceAnswer: trimmedReference === "" ? null : trimmedReference,
      explanation: explanation.trim() === "" ? null : explanation.trim(),
      difficulty: difficulty === NO_DIFFICULTY ? null : difficulty,
      section: section.trim() === "" ? null : section.trim(),
      options: null,
    };
  };

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const body = validate();
    if (!body) return;

    if (question) {
      updateMutation.mutate(
        { questionId: question.id, bankId, topicId, body },
        { onSuccess: () => onOpenChange(false) },
      );
    } else {
      createMutation.mutate(
        { bankId, topicId, body },
        { onSuccess: () => onOpenChange(false) },
      );
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={question ? "Редактировать вопрос" : "Новый вопрос"}
      description="Вопрос собственного банка тренажёра. Закрытые типы проверяются автоматически, развёрнутый — AI."
      onSubmit={handleSubmit}
      isPending={isPending}
      submitLabel={question ? "Сохранить" : "Добавить"}
      contentClassName="sm:max-w-[640px]"
    >
      <div className="space-y-1.5">
        <Label htmlFor="question-stem">Текст вопроса</Label>
        <Textarea
          id="question-stem"
          value={stem}
          onChange={(event) => setStem(event.target.value)}
          rows={3}
          maxLength={2000}
          placeholder="Например: Чем отличается Task от ValueTask?"
          disabled={isPending}
        />
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor="question-type">Тип вопроса</Label>
          <Select
            value={type}
            onValueChange={(value) => setType(value as TrainerQuestionType)}
            disabled={isPending}
          >
            <SelectTrigger id="question-type" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {QUIZ_QUESTION_TYPES.map((t) => (
                <SelectItem key={t} value={t}>
                  {TYPE_LABELS[t]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="question-difficulty">Сложность</Label>
          <Select value={difficulty} onValueChange={setDifficulty} disabled={isPending}>
            <SelectTrigger id="question-difficulty" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NO_DIFFICULTY}>Не задана</SelectItem>
              {TRAINER_QUESTION_DIFFICULTIES.map((d) => (
                <SelectItem key={d} value={d}>
                  {TRAINER_DIFFICULTY_VISUALS[d]?.label ?? d}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {isChoiceType(type) && (
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">
            Варианты ответа{" "}
            <span className="font-normal text-muted-foreground">
              (отметьте {type === "SINGLE_CHOICE" ? "правильный" : "правильные"})
            </span>
          </legend>
          <ul className="space-y-2">
            {options.map((option, index) => (
              <li key={option.key} className="flex items-center gap-2">
                <label
                  className="flex min-h-[44px] shrink-0 items-center justify-center px-1"
                  title="Правильный вариант"
                >
                  <Checkbox
                    checked={option.isCorrect}
                    onCheckedChange={(checked) =>
                      toggleOptionCorrect(option.key, checked === true)
                    }
                    disabled={isPending}
                    aria-label={`Вариант ${index + 1} правильный`}
                  />
                </label>
                <Input
                  value={option.text}
                  onChange={(event) => updateOptionText(option.key, event.target.value)}
                  placeholder={`Вариант ${index + 1}`}
                  disabled={isPending}
                  className="min-h-[44px] flex-1"
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="min-touch shrink-0 text-muted-foreground hover:text-destructive"
                  onClick={() => removeOption(option.key)}
                  disabled={isPending || options.length <= 2}
                  aria-label={`Удалить вариант ${index + 1}`}
                >
                  <Icons.delete className="size-4" />
                </Button>
              </li>
            ))}
          </ul>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={addOption}
            disabled={isPending}
            className="min-touch"
          >
            <Icons.add className="size-4" />
            Добавить вариант
          </Button>
        </fieldset>
      )}

      {isReferenceType(type) && (
        <div className="space-y-1.5">
          <Label htmlFor="question-reference">
            {type === "EXACT_TEXT" ? "Эталонный ответ" : "Эталонный ответ (для AI-проверки)"}
          </Label>
          <Textarea
            id="question-reference"
            value={referenceAnswer}
            onChange={(event) => setReferenceAnswer(event.target.value)}
            rows={2}
            maxLength={2000}
            placeholder={
              type === "EXACT_TEXT"
                ? "Точный ответ (сравнение без учёта регистра и пунктуации)"
                : "Опорный ответ — ориентир для AI-оценки (необязательно)"
            }
            disabled={isPending}
          />
        </div>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="question-explanation">Разбор (необязательно)</Label>
        <Textarea
          id="question-explanation"
          value={explanation}
          onChange={(event) => setExplanation(event.target.value)}
          rows={2}
          maxLength={2000}
          placeholder="Почему этот ответ верный — покажем после проверки"
          disabled={isPending}
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="question-section">Секция (необязательно)</Label>
        <Input
          id="question-section"
          value={section}
          onChange={(event) => setSection(event.target.value)}
          maxLength={120}
          placeholder="Например: async/await"
          disabled={isPending}
        />
      </div>
    </FormDialog>
  );
}
