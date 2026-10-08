"use client";

import { QuizOptionContent, stableShuffleOptions } from "@/entities/quiz";
import type {
  CheckAnswerResponse,
  TrainerFeedbackRating,
  TrainerSessionItem,
  TrainerSessionOption,
  TrainerVerdict,
} from "@/entities/trainer-session";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import type { ReactNode } from "react";

/**
 * Управление оценкой AI-разбора 👍/👎 (#691 t7) — прокидывается из раннера. `value` — текущая
 * (оптимистичная) оценка, `onRate` — клик по кнопке, `pending` — идёт ли запрос. Не передан →
 * кнопки не рисуются (review-контексты без обработчика).
 */
export interface FeedbackRatingControl {
  value: TrainerFeedbackRating | null;
  onRate: (rating: TrainerFeedbackRating) => void;
  pending: boolean;
}

/** Раскрытый результат проверки одного вопроса — общая форма для live-loop и review. */
export interface DrillRevealedAnswer {
  verdict: TrainerVerdict;
  /** Что выбрал/ввёл пользователь. */
  selectedOptionIds: string[];
  textAnswer: string;
  correctOptionIds: string[];
  referenceAnswer: string | null;
  explanation: string | null;
  /** Краткий AI-разбор открытого ответа (#585). `null` для авто-грейда / PENDING-самопроверки. */
  feedback: string | null;
}

interface DrillAnswerReviewProps {
  item: TrainerSessionItem;
  revealed: DrillRevealedAnswer;
  /** Оценка AI-разбора 👍/👎 (#691 t7). Не передана → кнопки скрыты. */
  feedbackControl?: FeedbackRatingControl;
}

/**
 * Разбор одного отвеченного вопроса DRILL-сессии (#568): вердикт + раскраска
 * выбранных/правильных вариантов (для choice/exact), эталон (для текстовых),
 * нейтральный блок «Пояснение». Используется и в live-loop (после «Проверить»),
 * и в review завершённой сессии. Зеркалит идиомы quiz-attempt-review.
 */
export function DrillAnswerReview({ item, revealed, feedbackControl }: DrillAnswerReviewProps) {
  const isChoice =
    item.questionType === "SINGLE_CHOICE" || item.questionType === "MULTI_CHOICE";
  const isPending = revealed.verdict === "PENDING";

  return (
    <div className="space-y-3">
      <VerdictLine verdict={revealed.verdict} />

      {isChoice && (
        <div className="grid gap-2">
          {stableShuffleOptions(item.questionId, item.options).map((option) => {
            const isCorrect = revealed.correctOptionIds.includes(option.id);
            const isSelected = revealed.selectedOptionIds.includes(option.id);
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
                <span className="min-w-0 flex-1">
                  <QuizOptionContent text={option.text} />
                  {isSelected && (
                    <span className="ml-1.5 text-xs text-muted-foreground">— твой выбор</span>
                  )}
                </span>
              </div>
            );
          })}
        </div>
      )}

      {!isChoice && (
        <div className="grid gap-2">
          <AnswerBox
            label="Твой ответ"
            value={revealed.textAnswer}
            mono={item.questionType === "EXACT_TEXT"}
            tone={
              item.questionType === "EXACT_TEXT"
                ? revealed.verdict === "CORRECT"
                  ? "correct"
                  : "incorrect"
                : "neutral"
            }
          />
          {revealed.referenceAnswer && (
            <div className="rounded-lg border border-primary/30 bg-primary/5 p-3">
              <p className="mb-1 text-xs font-medium text-primary">
                {item.questionType === "EXACT_TEXT" ? "Правильный ответ" : "Эталонный ответ"}
              </p>
              <p
                className={cn(
                  "whitespace-pre-wrap text-sm leading-snug",
                  item.questionType === "EXACT_TEXT" && "font-mono",
                )}
              >
                {revealed.referenceAnswer}
              </p>
            </div>
          )}
          {isPending && !revealed.referenceAnswer && (
            <p className="text-xs text-muted-foreground">
              Эталон не задан — сверься с материалом темы.
            </p>
          )}
        </div>
      )}

      <AiFeedback
        verdict={revealed.verdict}
        feedback={revealed.feedback}
        feedbackControl={feedbackControl}
      />

      <DrillExplanation explanation={revealed.explanation} />
    </div>
  );
}

/**
 * AI-разбор открытого ответа (#585): что было хорошо и чего не хватило. Показываем только когда AI
 * реально оценил ответ — на PENDING (фолбэк/самопроверка) фидбэка-разбора нет, там работает «Эталон».
 */
function AiFeedback({
  verdict,
  feedback,
  feedbackControl,
}: {
  verdict: TrainerVerdict;
  feedback: string | null;
  feedbackControl?: FeedbackRatingControl;
}) {
  if (verdict === "PENDING" || !feedback || feedback.trim().length === 0) return null;
  return (
    <div className="rounded-lg border border-primary/25 bg-primary/5 p-3">
      <div className="flex items-start gap-2">
        <Icons.ai className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
        <div className="min-w-0">
          <p className="mb-0.5 text-xs font-medium text-primary">Разбор ИИ</p>
          <p className="whitespace-pre-wrap text-sm leading-snug">{feedback}</p>
        </div>
      </div>
      {feedbackControl && <FeedbackRatingButtons control={feedbackControl} />}
    </div>
  );
}

/**
 * 👍/👎 на AI-разбор (#691 t7). Рисуются только когда есть разбор (этот компонент монтируется
 * внутри `AiFeedback`). Оптимистичная активная подсветка — из `control.value`. Mobile-first:
 * иконки-кнопки ≥44px тач-таргет, подпись «Полезен разбор?». Запросы дизейблятся на время `pending`.
 */
function FeedbackRatingButtons({ control }: { control: FeedbackRatingControl }) {
  return (
    <div className="mt-2.5 flex items-center gap-1.5 border-t border-primary/15 pt-2.5">
      <span className="mr-0.5 text-xs text-muted-foreground">Полезен разбор?</span>
      <RatingButton
        active={control.value === "UP"}
        disabled={control.pending}
        label="Полезный разбор"
        onClick={() => control.onRate("UP")}
        icon={<Icons.thumbsUp className="size-4" />}
        activeClass="border-green/50 bg-green/10 text-green"
      />
      <RatingButton
        active={control.value === "DOWN"}
        disabled={control.pending}
        label="Бесполезный разбор"
        onClick={() => control.onRate("DOWN")}
        icon={<Icons.thumbsDown className="size-4" />}
        activeClass="border-destructive/50 bg-destructive/10 text-destructive"
      />
    </div>
  );
}

function RatingButton({
  active,
  disabled,
  label,
  onClick,
  icon,
  activeClass,
}: {
  active: boolean;
  disabled: boolean;
  label: string;
  onClick: () => void;
  icon: ReactNode;
  activeClass: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      aria-pressed={active}
      title={label}
      className={cn(
        "inline-flex size-9 items-center justify-center rounded-md border transition-colors disabled:opacity-60",
        active
          ? activeClass
          : "border-border/60 bg-card/50 text-muted-foreground hover:bg-accent/40",
      )}
    >
      {icon}
    </button>
  );
}

function VerdictLine({ verdict }: { verdict: TrainerVerdict }) {
  if (verdict === "PENDING") {
    return (
      <p className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        <Icons.help className="size-3.5" />
        Самопроверка — сверься с эталоном ниже
      </p>
    );
  }
  // PARTIAL — частично верный открытый ответ (AI-грейдинг мока, #585).
  if (verdict === "PARTIAL") {
    return (
      <p className="flex items-center gap-1.5 text-sm font-medium text-amber-600 dark:text-amber-500">
        <Icons.warning className="size-4" />
        Частично верно — сверься с эталоном ниже
      </p>
    );
  }
  const correct = verdict === "CORRECT";
  return (
    <p
      className={cn(
        "flex items-center gap-1.5 text-sm font-medium",
        correct ? "text-green" : "text-destructive",
      )}
    >
      {correct ? <Icons.completed className="size-4" /> : <Icons.close className="size-4" />}
      {correct ? "Верно" : "Неверно — правильный вариант выделен зелёным"}
    </p>
  );
}

function AnswerBox({
  label,
  value,
  mono,
  tone,
}: {
  label: string;
  value: string;
  mono: boolean;
  tone: "correct" | "incorrect" | "neutral";
}) {
  return (
    <div
      className={cn(
        "rounded-lg border p-3",
        tone === "correct"
          ? "border-green/50 bg-green/10"
          : tone === "incorrect"
            ? "border-destructive/50 bg-destructive/10"
            : "border-border/60 bg-card/50",
      )}
    >
      <p className="mb-1 text-xs font-medium text-muted-foreground">{label}</p>
      <p className={cn("whitespace-pre-wrap text-sm leading-snug", mono && "font-mono")}>
        {value.trim() ? value : "—"}
      </p>
    </div>
  );
}

/** Нейтральный блок «Пояснение» (#561-стиль) — если у вопроса задан разбор. */
export function DrillExplanation({ explanation }: { explanation: string | null | undefined }) {
  if (!explanation || explanation.trim().length === 0) return null;
  return (
    <div className="flex items-start gap-2 rounded-lg border border-border/60 bg-muted/40 p-3">
      <Icons.lightbulb className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div className="min-w-0">
        <p className="mb-0.5 text-xs font-medium text-muted-foreground">Пояснение</p>
        <p className="whitespace-pre-wrap text-sm leading-snug">{explanation}</p>
      </div>
    </div>
  );
}

/**
 * Адаптер: ответ сервера (`CheckAnswerResponse`) + локальный черновик → `DrillRevealedAnswer`
 * (live-loop PER_QUESTION). Для голосового ответа транскрипта на клиенте нет — берём распознанный
 * текст из `response.answerText`; для текста/выбора он `null`, тогда показываем локальный черновик.
 * AI-фидбэк (`response.feedback`) прокидываем в разбор «Разбор ИИ» (#585).
 */
export function revealFromResponse(
  response: CheckAnswerResponse,
  answer: { selectedOptionIds: string[]; textAnswer: string },
): DrillRevealedAnswer {
  return {
    verdict: response.verdict,
    selectedOptionIds: answer.selectedOptionIds,
    textAnswer: response.answerText ?? answer.textAnswer,
    correctOptionIds: response.correctOptionIds ?? [],
    referenceAnswer: response.referenceAnswer,
    explanation: response.explanation,
    feedback: response.feedback,
  };
}

/** Адаптер: answered `TrainerSessionItem` → `DrillRevealedAnswer` (для review). */
export function revealFromItem(item: TrainerSessionItem): DrillRevealedAnswer {
  return {
    verdict: item.verdict ?? "PENDING",
    selectedOptionIds: parseAnswerOptionIds(item.answerRaw, item.options),
    textAnswer: parseAnswerText(item.answerRaw, item.options),
    correctOptionIds: item.correctOptionIds ?? [],
    referenceAnswer: item.referenceAnswer,
    explanation: item.explanation,
    feedback: item.feedback,
  };
}

/**
 * `answerRaw` в снапшоте — это либо CSV id'шников выбранных вариантов (choice),
 * либо сырой текст (exact/open). Если строка состоит из id'шников, известных
 * этому вопросу, трактуем как option-ids; иначе — как текст.
 */
function parseAnswerOptionIds(
  answerRaw: string | null,
  options: TrainerSessionOption[],
): string[] {
  if (!answerRaw) return [];
  const optionIds = new Set(options.map((option) => option.id));
  const parts = answerRaw.split(",").map((part) => part.trim());
  const matched = parts.filter((part) => optionIds.has(part));
  return matched.length > 0 ? matched : [];
}

function parseAnswerText(answerRaw: string | null, options: TrainerSessionOption[]): string {
  if (!answerRaw) return "";
  const optionIds = new Set(options.map((option) => option.id));
  const parts = answerRaw.split(",").map((part) => part.trim());
  const isOptionCsv = parts.length > 0 && parts.every((part) => optionIds.has(part));
  return isOptionCsv ? "" : answerRaw;
}
