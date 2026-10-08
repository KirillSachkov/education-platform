"use client";

import {
  isAnswerAlreadyChecked,
  type CheckAnswerResponse,
  type TrainerFeedbackRating,
  type TrainerSession,
  type TrainerSessionItem,
  type TrainerSessionSummary,
} from "@/entities/trainer-session";
import { useCheckAnswer } from "@/features/check-trainer-answer";
import { useCompleteSession } from "@/features/complete-drill-session";
import { useToggleBookmark } from "@/features/bookmark-question";
import { nextFeedbackRating, useRateAiFeedback } from "@/features/rate-ai-feedback";
import { canSelfAssess, useSelfAssessQuestion } from "@/features/self-assess-question";
import {
  deleteVoiceRecording,
  useSubmitVoiceAnswer,
  voiceRecordingKey,
} from "@/features/voice-answer";
import { cn } from "@/shared/lib/css";
import { isTrainerContentRedacted } from "@/shared/lib/trainer-redaction";
import { routes } from "@/shared/config/routes";
import { TRAINER_DIFFICULTY_VISUALS } from "@/shared/config/trainer";
import { LockCallout, LockedContentPlaceholder, ProgressBar } from "@/shared/ui/components";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";
import { useState } from "react";
import {
  DrillAnswerReview,
  revealFromItem,
  revealFromResponse,
  type DrillRevealedAnswer,
  type FeedbackRatingControl,
} from "./drill-answer-review";
import {
  DrillQuestionInput,
  EMPTY_DRILL_ANSWER,
  type DrillAnswerDraft,
} from "./drill-question-input";
import { DrillSummaryCard } from "./drill-summary-card";
import { MockResultsCard } from "./mock-results-card";
import { SessionTimer } from "./session-timer";
import { resolveSessionChrome } from "../lib/session-chrome";
import {
  hydrateRevealedAnswers,
  hydrateSubmittedAnswers,
  resolveReviewReveal,
} from "../lib/session-hydration";
import { shouldAutoSubmitOnLeave } from "../lib/should-auto-submit";

interface DrillSessionRunnerProps {
  session: TrainerSession;
  /** Куда возвращаться кнопкой «Назад к темам» (по умолчанию — лендинг). */
  backHref?: string;
  /** Явный ярлык режима в шапке — для review-сессий из списка вопросов (#656). */
  modeLabelOverride?: string;
}

/**
 * Раннер DRILL/MOCK-сессии (#568): один вопрос на экран. Завершённая сессия —
 * read-only разбор всех вопросов. В идущей сессии выбранный ответ сохраняется
 * автоматически при «Далее»/«Назад»/«Завершить» — отдельную кнопку жать не нужно (любой режим):
 *  - тренировка/вопросы/ошибки (PER_QUESTION): «Проверить» — опциональный мгновенный
 *    вердикт + разбор; без неё «Далее» всё равно сохранит ответ.
 *  - тест/симуляция (grade-at-end): вердикт/разбор скрыты до «Завершить».
 * Mobile-first, прогресс степпером + картой вопросов.
 */
export function DrillSessionRunner({
  session,
  backHref,
  modeLabelOverride,
}: DrillSessionRunnerProps) {
  const isReview = session.status !== "IN_PROGRESS";
  const isMock = session.mode === "MOCK";
  // Ярлык режима + цель «К теме». Review-сессии (клик по вопросу / «Доучить» / SRS /
  // закладки — все LEARN, по mode неотличимы от пачки-тренировки) передают явные
  // modeLabelOverride/backHref из точки запуска (#656); иначе выводятся из режима.
  const { modeLabel, backHref: resolvedBackHref } = resolveSessionChrome({
    mode: session.mode,
    topicIds: session.topicIds,
    backHref,
    modeLabelOverride,
  });
  // Grade-at-end тест (#568 Ф2): пока сессия идёт, ответ принимается «вслепую» —
  // вердикт/ключ скрыты до Complete (CheckAnswer вернёт PENDING + null-ключ).
  const isGradeAtEnd = session.revealPolicy === "END_OF_SESSION";
  const total = session.items.length;
  const checkAnswer = useCheckAnswer();
  const submitVoice = useSubmitVoiceAnswer();
  const completeSession = useCompleteSession();
  const toggleBookmark = useToggleBookmark();
  const rateFeedback = useRateAiFeedback();
  const selfAssess = useSelfAssessQuestion();
  // «Ответить» pending — любая из веток (текст/выбор check ИЛИ voice-upload).
  const isAnswerPending = checkAnswer.isPending || submitVoice.isPending;

  const [index, setIndex] = useState(() => firstUnansweredIndex(session.items));
  const [draft, setDraft] = useState<DrillAnswerDraft>(EMPTY_DRILL_ANSWER);
  // Раскрытые результаты, накопленные за сессию (live-loop, PER_QUESTION). itemId → reveal.
  // Resume/review hydration (#691 t4): seed already-answered items из снапшота, чтобы они
  // рендерились read-only с вердиктом — мастер не подаёт их как свежие (повтор → 409 → stuck).
  const [revealed, setRevealed] = useState<Record<string, DrillRevealedAnswer>>(() =>
    hydrateRevealedAnswers(session.items, !isGradeAtEnd || isReview),
  );
  // Принятые «вслепую» ответы grade-at-end (без раскрытия). itemId → true.
  // Grade-at-end ещё IN_PROGRESS → answered-снапшот восстанавливаем как blind-submitted
  // (вердикт скрыт до Complete).
  const [submitted, setSubmitted] = useState<Record<string, boolean>>(() =>
    hydrateSubmittedAnswers(session.items, isGradeAtEnd && !isReview),
  );
  const [bookmarked, setBookmarked] = useState<Record<string, boolean>>({});
  // Оптимистичная оценка AI-разбора (#691 t7). itemId → UP/DOWN/null. Бэкенд апсертит идемпотентно;
  // на ошибке откатываем к прошлому значению (как `bookmarked`).
  const [feedbackRatings, setFeedbackRatings] = useState<
    Record<string, TrainerFeedbackRating | null>
  >({});
  const [summary, setSummary] = useState<TrainerSessionSummary | null>(null);
  // Истёк ли таймер MOCK-сессии (#614 E) — приходит из SessionTimer. На нуле
  // включается hard-stop: ввод/запись блокируются, «Завершить» становится
  // primary. Сервер НЕ авто-фейлит — завершает пользователь.
  const [isTimeUp, setIsTimeUp] = useState(false);
  // Hard-stop активен только для идущей MOCK-сессии с таймером.
  const isHardStopped = isMock && !isReview && session.timeLimitSeconds != null && isTimeUp;

  const item = session.items[index];

  // Оценка AI-разбора 👍/👎 (#691 t7). Объявлено до ранних return'ов — итог-карта/review их используют.
  // Клик по активной кнопке — no-op (бэкенд не снимает оценку), по противоположной — переключение.
  // Оптимистично ставим сразу, на ошибке откатываем. Не зависит от текущего `item` — берёт itemId.
  const handleRateFeedback = (itemId: string) => (clicked: TrainerFeedbackRating) => {
    if (rateFeedback.isPending) return;
    const current = feedbackRatings[itemId] ?? null;
    const next = nextFeedbackRating(current, clicked);
    if (next === null) return;
    setFeedbackRatings((prev) => ({ ...prev, [itemId]: next }));
    rateFeedback.mutate(
      { sessionId: session.id, itemId, rating: next },
      { onError: () => setFeedbackRatings((prev) => ({ ...prev, [itemId]: current })) },
    );
  };

  const feedbackControlFor = (itemId: string): FeedbackRatingControl => ({
    value: feedbackRatings[itemId] ?? null,
    onRate: handleRateFeedback(itemId),
    pending: rateFeedback.isPending,
  });

  // Завершённый мок (только что или открыт повторно) → AI-grade-aware карточка
  // итога: сама поллит грейдинг и раскрывает пер-вопросный разбор + «Разбор ИИ».
  if (isMock && (summary || isReview)) {
    return <MockResultsCard sessionId={session.id} />;
  }

  if (summary) {
    return (
      <div className="mx-auto w-full max-w-3xl space-y-6">
        <DrillSummaryCard summary={summary} backHref={resolvedBackHref} mode={session.mode} />
        <DrillResultsReview
          items={session.items}
          revealed={revealed}
          feedbackControlFor={feedbackControlFor}
        />
      </div>
    );
  }

  if (total === 0 || !item) {
    return (
      <div className="mx-auto w-full max-w-3xl rounded-xl border border-border/60 bg-card p-6 text-center">
        <p className="text-sm text-muted-foreground">В этой тренировке нет вопросов.</p>
        <Button asChild variant="outline" className="mt-4">
          <Link href={resolvedBackHref}>
            <Icons.chevronLeft className="size-4" />К теме
          </Link>
        </Button>
      </div>
    );
  }

  // Раскрытый результат текущего вопроса: из live-стейта или (в review) из item.
  const itemReveal: DrillRevealedAnswer | null = isReview
    ? item.isAnswered
      ? revealFromItem(item)
      : null
    : (revealed[item.id] ?? null);
  const isItemChecked = itemReveal !== null;
  // Grade-at-end: ответ принят «вслепую» (без раскрытия). Из live-стейта или из
  // снапшота (item.isAnswered) при resume незавершённой grade-at-end-сессии.
  const isItemSubmitted = isGradeAtEnd && !isReview && (submitted[item.id] || item.isAnswered);
  const isBookmarked = bookmarked[item.id] ?? false;

  const sectionLabel = item.section;
  const difficultyVisual = item.difficulty ? TRAINER_DIFFICULTY_VISUALS[item.difficulty] : null;

  // OPEN_TEXT отвечается голосом ИЛИ текстом (#585) — активный режим в draft.
  const isOpenTextVoice = item.questionType === "OPEN_TEXT" && draft.voiceMode === "voice";

  const canCheck = (): boolean => {
    // OPEN_TEXT голосом — нужна запись; OPEN_TEXT текстом / EXACT_TEXT — текст.
    if (isOpenTextVoice) return draft.audioBlob !== null;
    if (item.questionType === "OPEN_TEXT" || item.questionType === "EXACT_TEXT") {
      return draft.textAnswer.trim().length > 0;
    }
    return draft.selectedOptionIds.length > 0;
  };

  // Раскрытие/принятие результата — общая ветка для check (текст/выбор) и
  // voice-upload: оба возвращают CheckAnswerResponse. Для голоса транскрипта на
  // клиенте нет — берём распознанный текст из ответа сервера (response.answerText).
  const applyAnswerResult = (
    response: CheckAnswerResponse,
    answer: { selectedOptionIds: string[]; textAnswer: string },
  ) => {
    // Ответ отправлен → персист голосовой записи этого item'а больше не нужен.
    void deleteVoiceRecording(voiceRecordingKey(session.id, item.id));
    // Grade-at-end: сервер вернул PENDING + null-ключ (раскрытие — после
    // Complete). Не раскрываем — лишь помечаем «принято», даём идти дальше.
    if (isGradeAtEnd) {
      setSubmitted((prev) => ({ ...prev, [item.id]: true }));
      return;
    }
    setRevealed((prev) => ({ ...prev, [item.id]: revealFromResponse(response, answer) }));
  };

  /**
   * 409 «ответ уже проверен» (#691 t4) — это НЕ ошибка, а success-path: сервер уже хранит
   * ответ. Раскрываем его из снапшота (или помечаем blind-submitted для grade-at-end) и
   * пускаем навигацию дальше — без блокирующего тоста и без застрявшего мастера. Зеркалит
   * applyAnswerResult. На любой другой ошибке — no-op (тост уже показал хук, навигация
   * остаётся на месте). Возвращает true, если 409 обработан.
   */
  const handleAnswerError = (error: unknown, onDone?: () => void): boolean => {
    if (!isAnswerAlreadyChecked(error)) return false;
    void deleteVoiceRecording(voiceRecordingKey(session.id, item.id));
    if (isGradeAtEnd) {
      setSubmitted((prev) => ({ ...prev, [item.id]: true }));
    } else {
      setRevealed((prev) => ({ ...prev, [item.id]: revealFromItem(item) }));
    }
    onDone?.();
    return true;
  };

  /**
   * Отправка текущего черновика ответа (выбор/текст/голос) → CheckAnswer/SubmitVoice.
   * `onDone` вызывается после успешной отправки — для авто-сохранения при переходе в тесте.
   */
  const submitDraft = (onDone?: () => void) => {
    if (isAnswerPending) return;
    // OPEN_TEXT голосом → upload аудио (сервер сам транскрибирует + грейдит).
    if (isOpenTextVoice) {
      if (!draft.audioBlob) {
        onDone?.();
        return;
      }
      submitVoice.mutate(
        { sessionId: session.id, itemId: item.id, audio: draft.audioBlob },
        {
          onSuccess: (response) => {
            applyAnswerResult(response, { selectedOptionIds: [], textAnswer: "" });
            onDone?.();
          },
          onError: (error) => handleAnswerError(error, onDone),
        },
      );
      return;
    }
    // Текст / выбор → проверка ответа.
    const request =
      item.questionType === "OPEN_TEXT" || item.questionType === "EXACT_TEXT"
        ? { text: draft.textAnswer.trim() }
        : { optionIds: draft.selectedOptionIds };
    checkAnswer.mutate(
      { sessionId: session.id, itemId: item.id, request },
      {
        onSuccess: (response) => {
          applyAnswerResult(response, {
            selectedOptionIds: draft.selectedOptionIds,
            textAnswer: draft.textAnswer.trim(),
          });
          onDone?.();
        },
        onError: (error) => handleAnswerError(error, onDone),
      },
    );
  };

  // Явная проверка (PER_QUESTION «Проверить»): отправляем и сразу раскрываем разбор.
  const handleCheck = () => submitDraft();

  const goTo = (next: number) => {
    setIndex(next);
    setDraft(EMPTY_DRILL_ANSWER);
  };

  // Переход между вопросами. Во ВСЕХ режимах (тест/тренировка/ошибки/симуляция)
  // «Далее»/«Назад»/карта сохраняют выбранный, но не зафиксированный ответ ПЕРЕД переходом —
  // отдельную кнопку жать не нужно (#568). В PER_QUESTION «Проверить» остаётся опциональным
  // мгновенным разбором, но для записи ответа не требуется.
  const navigateTo = (next: number) => {
    if (
      shouldAutoSubmitOnLeave({
        isReview,
        isHardStopped,
        isItemRecorded: isItemChecked || isItemSubmitted,
        isAnswerPending,
        hasAnswerDraft: canCheck(),
      })
    ) {
      submitDraft(() => goTo(next));
    } else {
      goTo(next);
    }
  };

  const handleComplete = () => {
    if (completeSession.isPending || isAnswerPending) return;
    const finish = () =>
      completeSession.mutate(session.id, { onSuccess: (result) => setSummary(result) });
    // Тест: сохранить ответ последнего вопроса перед завершением.
    if (
      shouldAutoSubmitOnLeave({
        isReview,
        isHardStopped,
        isItemRecorded: isItemChecked || isItemSubmitted,
        isAnswerPending,
        hasAnswerDraft: canCheck(),
      })
    ) {
      submitDraft(finish);
    } else {
      finish();
    }
  };

  const handleToggleBookmark = () => {
    if (toggleBookmark.isPending) return;
    const nextState = !isBookmarked;
    setBookmarked((prev) => ({ ...prev, [item.id]: nextState }));
    toggleBookmark.mutate(
      { topicId: item.topicId, questionId: item.questionId, isBookmarked },
      {
        onError: () => setBookmarked((prev) => ({ ...prev, [item.id]: !nextState })),
      },
    );
  };

  const isLast = index === total - 1;

  // «Не уверен» (#691 t8) — мягкая самооценка: вопрос уходит в REVIEW («на повтор»), без пометки
  // «неверно» и без движения mastery. Тот же гейт, что у «Проверить» (неотвеченный вопрос в
  // интерактивном PER_QUESTION-режиме). По нажатию — пометить на сервере и пойти дальше, как с ответом.
  const canSoftAssess = canSelfAssess({
    isReview,
    isHardStopped,
    isGradeAtEnd,
    isItemChecked,
    isItemSubmitted,
    isLocked: item.isLocked,
  });

  const handleSelfAssess = () => {
    if (selfAssess.isPending || isAnswerPending) return;
    selfAssess.mutate(
      { sessionId: session.id, itemId: item.id },
      { onSuccess: () => !isLast && goTo(index + 1) },
    );
  };

  return (
    <div className="mx-auto w-full max-w-3xl space-y-4">
      <Button
        asChild
        variant="ghost"
        size="sm"
        className="-ml-2 h-auto self-start py-1 text-muted-foreground"
      >
        <Link href={resolvedBackHref}>
          <Icons.chevronLeft className="size-4" />
          {isMock ? "К симуляции" : "К теме"}
        </Link>
      </Button>

      {/* Степпер + карта вопросов */}
      <div className="space-y-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
            <span className="inline-flex items-center rounded-md bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
              {modeLabel}
            </span>
            Вопрос {index + 1} из {total}
          </p>
          {isMock && session.timeLimitSeconds != null && (
            <SessionTimer
              startedAt={session.startedAt}
              limitSeconds={session.timeLimitSeconds}
              frozen={isReview}
              onExpiredChange={setIsTimeUp}
            />
          )}
        </div>
        <ProgressBar value={((index + 1) / total) * 100} />

        <QuestionMap
          items={session.items}
          currentIndex={index}
          isAnswered={(qItem) =>
            isReview
              ? qItem.isAnswered
              : isGradeAtEnd
                ? submitted[qItem.id] === true || qItem.isAnswered
                : revealed[qItem.id] !== undefined
          }
          onJump={navigateTo}
        />
      </div>

      {/* Вопрос */}
      <div className="rounded-xl border border-border/60 bg-card p-4 sm:p-6">
        {(difficultyVisual || sectionLabel) && (
          <div className="mb-3 flex flex-wrap items-center justify-end gap-1.5">
            {difficultyVisual && (
              <span
                className={cn(
                  "inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium",
                  difficultyVisual.badgeClass,
                )}
              >
                {difficultyVisual.label}
              </span>
            )}
            {sectionLabel && (
              <Badge variant="outline" className="font-normal text-muted-foreground">
                {sectionLabel}
              </Badge>
            )}
          </div>
        )}
        {isTrainerContentRedacted(item.isLocked, item.questionText) ? (
          <LockedContentPlaceholder lines={3} />
        ) : (
          <MarkdownContent
            variant="compact"
            disableLinks
            className="min-w-0 [&>*:first-child]:mt-0 [&>*:last-child]:mb-0 [&_pre]:overflow-x-auto"
          >
            {item.questionText as string}
          </MarkdownContent>
        )}

        <div className="mt-5">
          {item.isLocked ? (
            <LockCallout reason={item.lockReason ?? "pro_required"} ctaHref={routes.trainerPro} />
          ) : isItemChecked && itemReveal ? (
            <DrillAnswerReview
              item={item}
              revealed={itemReveal}
              feedbackControl={feedbackControlFor(item.id)}
            />
          ) : isItemSubmitted ? (
            <p className="flex items-center gap-2 rounded-lg border border-border/60 bg-muted/40 p-3 text-sm text-muted-foreground">
              <Icons.check className="size-4 shrink-0 text-green" />
              Ответ принят — проверка и разбор будут в конце теста.
            </p>
          ) : isHardStopped ? (
            <p className="flex items-center gap-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
              <Icons.clock className="size-4 shrink-0" />
              Время вышло — заверши симуляцию, чтобы увидеть разбор.
            </p>
          ) : (
            <DrillQuestionInput
              item={item}
              draft={draft}
              disabled={isAnswerPending}
              onChange={setDraft}
              persistKey={voiceRecordingKey(session.id, item.id)}
            />
          )}
        </div>

        {/* Кнопки действий */}
        <div className="mt-5 flex flex-wrap items-center gap-2">
          {/* В тесте (grade-at-end) отдельной «Ответить» нет — ответ сохраняется при «Далее». */}
          {!isReview &&
            !isHardStopped &&
            !isGradeAtEnd &&
            !isItemChecked &&
            !isItemSubmitted &&
            !item.isLocked && (
              <Button type="button" onClick={handleCheck} disabled={isAnswerPending || !canCheck()}>
                {isAnswerPending && <Icons.loading className="size-4 animate-spin" />}
                Проверить
              </Button>
            )}
          {/* «Не уверен» (#691 t8) — мягкая самооценка рядом с «Проверить»: отложить вопрос на повтор. */}
          {canSoftAssess && (
            <Button
              type="button"
              variant="outline"
              onClick={handleSelfAssess}
              disabled={selfAssess.isPending || isAnswerPending}
            >
              {selfAssess.isPending ? (
                <Icons.loading className="size-4 animate-spin" />
              ) : (
                <Icons.help className="size-4" />
              )}
              Не уверен
            </Button>
          )}
          {(isItemChecked || isItemSubmitted) && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handleToggleBookmark}
              disabled={toggleBookmark.isPending}
              aria-pressed={isBookmarked}
            >
              {isBookmarked ? (
                <Icons.bookmarkFilled className="size-4 text-primary" />
              ) : (
                <Icons.bookmark className="size-4" />
              )}
              {isBookmarked ? "В закладках" : "В закладки"}
            </Button>
          )}
        </div>
      </div>

      {/* Навигация */}
      <div className="flex items-center justify-between gap-3">
        <Button
          type="button"
          variant="outline"
          disabled={index === 0 || isAnswerPending}
          onClick={() => navigateTo(Math.max(0, index - 1))}
        >
          <Icons.chevronLeft className="size-4" />
          Назад
        </Button>

        {isHardStopped ? (
          // Время вышло (#614 E): «Завершить» — primary с любого вопроса, без
          // оглядки на answered-гейт. «Далее/Назад» остаются для пролистывания
          // накопленных ответов перед завершением.
          <div className="flex items-center gap-2">
            {!isLast && (
              <Button
                type="button"
                variant="outline"
                onClick={() => goTo(Math.min(total - 1, index + 1))}
              >
                Далее
                <Icons.chevronRight className="size-4" />
              </Button>
            )}
            <Button type="button" onClick={handleComplete} disabled={completeSession.isPending}>
              {completeSession.isPending && <Icons.loading className="size-4 animate-spin" />}
              Завершить симуляцию
            </Button>
          </div>
        ) : isLast ? (
          isReview ? (
            <Button asChild variant="outline">
              <Link href={resolvedBackHref}>
                <Icons.chevronLeft className="size-4" />К теме
              </Link>
            </Button>
          ) : (
            <Button
              type="button"
              onClick={handleComplete}
              disabled={
                completeSession.isPending ||
                isAnswerPending ||
                (!isItemChecked &&
                  !isItemSubmitted &&
                  !canCheck() &&
                  !hasAnyAnswered(revealed, submitted))
              }
            >
              {(completeSession.isPending || isAnswerPending) && (
                <Icons.loading className="size-4 animate-spin" />
              )}
              {isGradeAtEnd ? "Завершить и проверить" : "Завершить"}
            </Button>
          )
        ) : (
          <Button
            type="button"
            disabled={isAnswerPending}
            onClick={() => navigateTo(Math.min(total - 1, index + 1))}
          >
            {isAnswerPending && <Icons.loading className="size-4 animate-spin" />}
            Далее
            <Icons.chevronRight className="size-4" />
          </Button>
        )}
      </div>
    </div>
  );
}

/**
 * Полный разбор завершённого теста/тренировки (#568): все вопросы со своими ответами
 * и разбором сразу на экране итога. Раскрытия берём из live-стейта PER_QUESTION-цикла;
 * пропущенные вопросы помечаем «без ответа».
 */
function DrillResultsReview({
  items,
  revealed,
  feedbackControlFor,
}: {
  items: TrainerSessionItem[];
  revealed: Record<string, DrillRevealedAnswer>;
  feedbackControlFor: (itemId: string) => FeedbackRatingControl;
}) {
  return (
    <section className="space-y-3">
      <h2 className="text-base font-semibold tracking-tight">Разбор ответов</h2>
      {items.map((item, idx) => {
        // #664-E: ключ «отвечен» — это `item.isAnswered`, НЕ наличие live-раскрытия. Answered
        // OPEN_TEXT несёт verdict PENDING (самопроверка) — его всё равно показываем разбором,
        // а не «без ответа». Fallback на снапшот, когда live-стейта нет (resume / grade-at-end).
        const reveal = resolveReviewReveal(item, revealed[item.id]);
        const difficultyVisual = item.difficulty
          ? TRAINER_DIFFICULTY_VISUALS[item.difficulty]
          : null;
        return (
          <div key={item.id} className="rounded-xl border border-border/60 bg-card p-4 sm:p-5">
            <div className="mb-3 flex items-center justify-between gap-2">
              <span className="text-xs font-medium text-muted-foreground">Вопрос {idx + 1}</span>
              {difficultyVisual && (
                <span
                  className={cn(
                    "inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium",
                    difficultyVisual.badgeClass,
                  )}
                >
                  {difficultyVisual.label}
                </span>
              )}
            </div>
            {isTrainerContentRedacted(item.isLocked, item.questionText) ? (
              <LockedContentPlaceholder lines={2} />
            ) : (
              <MarkdownContent
                variant="compact"
                disableLinks
                className="[&>*:first-child]:mt-0 [&>*:last-child]:mb-0 [&_pre]:overflow-x-auto [&_pre_code]:!whitespace-pre"
              >
                {item.questionText as string}
              </MarkdownContent>
            )}
            <div className="mt-4">
              {item.isLocked ? (
                <LockCallout
                  reason={item.lockReason ?? "pro_required"}
                  ctaHref={routes.trainerPro}
                />
              ) : reveal ? (
                <DrillAnswerReview
                  item={item}
                  revealed={reveal}
                  feedbackControl={feedbackControlFor(item.id)}
                />
              ) : (
                <p className="text-sm text-muted-foreground">Вопрос остался без ответа.</p>
              )}
            </div>
          </div>
        );
      })}
    </section>
  );
}

function QuestionMap({
  items,
  currentIndex,
  isAnswered,
  onJump,
}: {
  items: TrainerSessionItem[];
  currentIndex: number;
  isAnswered: (item: TrainerSessionItem) => boolean;
  onJump: (index: number) => void;
}) {
  return (
    <div className="flex flex-wrap gap-1.5 pt-1">
      {items.map((qItem, itemIndex) => {
        const answered = isAnswered(qItem);
        const current = itemIndex === currentIndex;
        return (
          <button
            key={qItem.id}
            type="button"
            onClick={() => onJump(itemIndex)}
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
  );
}

function firstUnansweredIndex(items: TrainerSessionItem[]): number {
  const idx = items.findIndex((item) => !item.isAnswered);
  return idx === -1 ? 0 : idx;
}

function hasAnyAnswered(
  revealed: Record<string, DrillRevealedAnswer>,
  submitted: Record<string, boolean>,
): boolean {
  return (
    Object.keys(revealed).length > 0 || Object.values(submitted).some((value) => value === true)
  );
}
