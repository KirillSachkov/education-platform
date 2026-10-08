"use client";

import { QuizOptionContent, stableShuffleOptions } from "@/entities/quiz";
import type { TrainerSessionItem } from "@/entities/trainer-session";
import { VoiceAnswerInput, type VoiceAnswerMode } from "@/features/voice-answer";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { LockCallout } from "@/shared/ui/components";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Input } from "@/shared/ui/kit/input";
import { RadioGroup, RadioGroupItem } from "@/shared/ui/kit/radio-group";
import type { Dispatch, SetStateAction } from "react";

export interface DrillAnswerDraft {
  selectedOptionIds: string[];
  textAnswer: string;
  /**
   * Активный способ открытого ответа (#585): `voice` → на «Ответить» уходит
   * `audioBlob`, `text` → `textAnswer`. По умолчанию `voice` (тумблер стартует
   * на записи). Не-OPEN_TEXT типы игнорируют это поле.
   */
  voiceMode: VoiceAnswerMode;
  /** Записанный голосовой Blob (#585). null — записи ещё нет. */
  audioBlob: Blob | null;
}

export const EMPTY_DRILL_ANSWER: DrillAnswerDraft = {
  selectedOptionIds: [],
  textAnswer: "",
  voiceMode: "voice",
  audioBlob: null,
};

interface DrillQuestionInputProps {
  item: TrainerSessionItem;
  draft: DrillAnswerDraft;
  disabled: boolean;
  /**
   * Сеттер черновика (как `setState`): принимает значение ИЛИ функцию-апдейтер.
   * OPEN_TEXT-ветка использует апдейтер, чтобы голос/режим/текст мёржились на
   * последнем стейте (два независимых колбэка `VoiceAnswerInput` не перетирают
   * друг друга при батчинге).
   */
  onChange: Dispatch<SetStateAction<DrillAnswerDraft>>;
  /** Ключ персиста голосовой записи (`session:item`) — переживает навигацию/refresh (#585). */
  persistKey?: string;
}

/**
 * Ввод ответа на вопрос DRILL-сессии: SINGLE_CHOICE — radio, MULTI_CHOICE —
 * checkbox, EXACT_TEXT — однострочный моно-инпут, OPEN_TEXT — голос/текст
 * (`VoiceAnswerInput`, #585). Зеркалит идиомы level-test/quiz-раннеров (#568).
 * Варианты стабильно перемешаны по questionId — правильный не оседает первым
 * (грейдинг по id).
 */
export function DrillQuestionInput({
  item,
  draft,
  disabled,
  onChange,
  persistKey,
}: DrillQuestionInputProps) {
  // Монетизация по типу вопроса (#623): развёрнутый (OPEN_TEXT/голос → AI) вопрос за PRO. Free
  // видит стем (выше по дереву) + замок вместо инпута. Закрытые тесты никогда не locked.
  if (item.isLocked) {
    return (
      <div className="rounded-xl border border-border/60 bg-card p-4 sm:p-5">
        <LockCallout reason={item.lockReason ?? "pro_required"} ctaHref={routes.pricing} />
      </div>
    );
  }

  const displayOptions = stableShuffleOptions(item.questionId, item.options);

  if (item.questionType === "SINGLE_CHOICE") {
    return (
      <RadioGroup
        value={draft.selectedOptionIds[0] ?? ""}
        onValueChange={(optionId) =>
          onChange({ ...draft, selectedOptionIds: [optionId], textAnswer: "" })
        }
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

  if (item.questionType === "MULTI_CHOICE") {
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
                onCheckedChange={(value) => {
                  const selectedOptionIds =
                    value === true
                      ? [...draft.selectedOptionIds, option.id]
                      : draft.selectedOptionIds.filter((id) => id !== option.id);
                  onChange({ ...draft, selectedOptionIds, textAnswer: "" });
                }}
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

  if (item.questionType === "EXACT_TEXT") {
    return (
      <div className="grid gap-1.5">
        <Input
          value={draft.textAnswer}
          onChange={(event) =>
            onChange({ ...draft, selectedOptionIds: [], textAnswer: event.target.value })
          }
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
    );
  }

  // OPEN_TEXT (и неизвестные типы) — голос/текст. В моке (END_OF_SESSION) ответ
  // уйдёт на сервер: голос → upload аудио (сервер сам транскрибирует + грейдит),
  // текст → обычный check. Раннер по `voiceMode`/`audioBlob` выбирает submit-ветку.
  return (
    <VoiceAnswerInput
      key={item.id}
      persistKey={persistKey}
      value={draft.textAnswer}
      onChange={(next) =>
        onChange((prev) => ({ ...prev, selectedOptionIds: [], textAnswer: next }))
      }
      onVoiceRecorded={(audioBlob) =>
        onChange((prev) => ({ ...prev, selectedOptionIds: [], audioBlob }))
      }
      onModeChange={(voiceMode) => onChange((prev) => ({ ...prev, voiceMode }))}
      disabled={disabled}
    />
  );
}
