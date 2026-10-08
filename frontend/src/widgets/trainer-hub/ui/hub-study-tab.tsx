"use client";

import { type TrainerTopicListItem } from "@/entities/trainer-topic";
import { trainerQuestionsQueryOptions } from "@/entities/trainer-question";
import { useStartLearn } from "@/features/start-learn-session";
import { QuestionList } from "@/features/trainer-question-list";
import { routes } from "@/shared/config/routes";
import { getMasteryTone } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { LockCallout, SegmentedControl, type SegmentedOption } from "@/shared/ui/components";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { type StudySubmode } from "../lib/hub-state";
import { useTrainerLoginGate } from "../lib/use-trainer-login-gate";
import { LessonListPanel } from "./lesson-list-panel";

interface HubStudyTabProps {
  isAuthenticated: boolean;
  /** Выбранная тема (topicId) из URL, либо null — показываем пикер. */
  selectedTopicId: string | null;
  /** Под-режим: вопросы (`list`) | тренировка (`learn`). */
  submode: StudySubmode;
  /** Карта topicId → тема (резолв названия/доступа выбранной темы). */
  topicMap: Map<string, TrainerTopicListItem>;
  /** Выбрать тему → хаб пишет `?topic=`. */
  onSelectTopic: (topicId: string) => void;
  /** Сбросить выбор темы → назад к пикеру. */
  onClearTopic: () => void;
  /** Переключить под-режим (list/learn). */
  onSubmodeChange: (submode: StudySubmode) => void;
  /** Запустить ТЕСТ по вопросу(ам) — хаб стартует review-сессию и навигирует. */
  onLaunchTest: (questionIds: string[]) => void;
  /** Отрисовать пикер темы (живёт в хабе). */
  renderTopicPicker: (props: {
    title: string;
    hint?: string;
    hideLocked?: boolean;
    onPick: (topic: TrainerTopicListItem) => void;
  }) => React.ReactNode;
}

const SUBMODE_OPTIONS: SegmentedOption<StudySubmode>[] = [
  { value: "list", label: "Вопросы" },
  { value: "learn", label: "Тренировка" },
  { value: "test", label: "Тест" },
];

/**
 * Вкладка «Обучение» хаба (#568 Ф2/Ф3; слияние «Изучение»+«Тесты»). Сначала пикер
 * темы; после выбора — три под-режима внутри темы:
 *  - «Вопросы» — список охвата темы со статусами → клик запускает ТЕСТ по вопросу
 *    (мгновенная проверка + разбор; открытые AI-грейдятся). Без самопроверки.
 *  - «Тренировка» — случайная пачка вопросов с мгновенным разбором
 *    (`start-learn-session` + раннер LEARN). Без записываемого балла.
 *  - «Тест» — уровневые наборы (grade-at-end): ответы и разбор открываются после
 *    завершения (`LessonListPanel`, `RevealPolicy=END_OF_SESSION`).
 * Де-иконенный минимализм.
 */
export function HubStudyTab({
  isAuthenticated,
  selectedTopicId,
  submode,
  topicMap,
  onSelectTopic,
  onClearTopic,
  onSubmodeChange,
  onLaunchTest,
  renderTopicPicker,
}: HubStudyTabProps) {
  // Аноним просматривает темы/вопросы read-only (#614 F); действия (открыть карточку,
  // тренировка, тест) гейтятся login-CTA внутри панелей. Без блокирующей заглушки.
  const selectedTopic = selectedTopicId ? topicMap.get(selectedTopicId) : undefined;

  if (!selectedTopicId) {
    return renderTopicPicker({
      title: "Выбери тему для изучения",
      hint: "Карточки с разбором, набор тестов по уровням или тренировка случайной пачкой.",
      // #614 B2: PRO-темы показываем с замком + paywall (фримиум-воронка), не прячем.
      hideLocked: false,
      onPick: (topic) => onSelectTopic(topic.id),
    });
  }

  return (
    <div className="mx-auto w-full max-w-3xl space-y-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-center gap-2">
          <Button
            variant="ghost"
            size="sm"
            className="-ml-2 shrink-0 text-muted-foreground"
            onClick={onClearTopic}
          >
            <Icons.chevronLeft className="size-4" />
            Темы
          </Button>
          <h2 className="truncate text-base font-semibold tracking-tight" title={selectedTopic?.title}>
            {selectedTopic?.title ?? "Тема"}
          </h2>
        </div>
        <SegmentedControl
          ariaLabel="Под-режим изучения"
          options={SUBMODE_OPTIONS}
          value={submode}
          onChange={onSubmodeChange}
          className="self-start"
        />
      </div>

      <TopicProgressStrip topicId={selectedTopicId} />

      {submode === "list" && (
        <QuestionList
          key={selectedTopicId}
          topicId={selectedTopicId}
          isAuthenticated={isAuthenticated}
          onOpenCard={(questionId) => onLaunchTest([questionId])}
        />
      )}
      {submode === "learn" && (
        <LearnByTestPanel topicId={selectedTopicId} topicLocked={selectedTopic?.isLocked ?? false} />
      )}
      {submode === "test" && (
        <LessonListPanel topicId={selectedTopicId} topicLocked={selectedTopic?.isLocked ?? false} />
      )}
    </div>
  );
}

/**
 * Размеры тренировки под число вопросов темы (≥10): пресеты 5/10/15 строго меньше
 * доступного + «Все (N)». Темы меньше 10 идут отдельной веткой (без разбивки).
 */
function buildLearnChoices(total: number): { value: number; label: string }[] {
  const presets = [5, 10, 15].filter((count) => count < total);
  return [
    ...presets.map((count) => ({ value: count, label: String(count) })),
    { value: total, label: `Все (${total})` },
  ];
}

/**
 * Под-режим «Тренировка» (#568 Ф3): случайная пачка вопросов темы для разминки.
 * Размеры адаптируются к числу вопросов темы — пресеты только меньше доступного
 * + «Все»; если вопросов < 10, разбивки нет, одна кнопка по всем. Старт LEARN →
 * редирект на раннер (мгновенный фидбэк). Без записываемого балла. Locked → CTA.
 */
function LearnByTestPanel({ topicId, topicLocked }: { topicId: string; topicLocked: boolean }) {
  const router = useRouter();
  const { requireAuth } = useTrainerLoginGate();
  const startLearn = useStartLearn();
  const listQuery = useQuery(trainerQuestionsQueryOptions.listOptions(topicId));
  const [picked, setPicked] = useState<number | null>(null);

  if (topicLocked) {
    return (
      <div className="rounded-xl border border-border/60 bg-card p-5 sm:p-6">
        <LockCallout reason="pro_required" ctaHref={routes.trainerPro} />
      </div>
    );
  }

  const total = listQuery.data?.items.length ?? 0;
  const showPicker = total >= 10;
  const choices = showPicker ? buildLearnChoices(total) : [];
  const choiceValues = choices.map((choice) => choice.value);
  // Дефолтная подсветка: явный выбор, иначе 10 (если в наборе), иначе «Все».
  const selectedCount =
    picked != null && choiceValues.includes(picked)
      ? picked
      : choiceValues.includes(10)
        ? 10
        : (choices.at(-1)?.value ?? total);

  const handleStart = () => {
    if (!requireAuth()) return;
    if (startLearn.isPending || total === 0) return;
    startLearn.mutate(
      { topicId, questionCount: showPicker ? selectedCount : total },
      { onSuccess: (created) => router.push(routes.trainerSession(created.id)) },
    );
  };

  return (
    <div className="space-y-4 rounded-xl border border-border/60 bg-card p-5 sm:p-6">
      <div className="space-y-1">
        <p className="text-sm font-medium">Тренировка</p>
        <p className="text-sm text-muted-foreground">
          Случайная пачка вопросов темы — быстро размяться, с мгновенным разбором. Ошибочные
          вернутся, пока не закрепишь. Без записываемого балла.
        </p>
      </div>

      {listQuery.isPending ? (
        <Skeleton className="h-11 w-56 rounded-lg" />
      ) : total === 0 ? (
        <p className="text-sm text-muted-foreground">В этой теме пока нет вопросов.</p>
      ) : (
        <>
          {showPicker ? (
            <div className="space-y-2">
              <p className="text-sm font-medium">Сколько вопросов?</p>
              <div className="flex flex-wrap gap-2">
                {choices.map((choice) => (
                  <button
                    key={choice.value}
                    type="button"
                    onClick={() => setPicked(choice.value)}
                    aria-pressed={selectedCount === choice.value}
                    className={cn(
                      "min-h-[44px] min-w-[64px] rounded-lg border px-4 text-sm font-medium tabular-nums transition-colors",
                      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
                      selectedCount === choice.value
                        ? "border-primary bg-primary/10 text-primary"
                        : "border-border/60 bg-card/50 text-foreground/80 hover:bg-accent/40",
                    )}
                  >
                    {choice.label}
                  </button>
                ))}
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              В теме {total} вопр. — тренировка пройдёт по всем.
            </p>
          )}

          <Button onClick={handleStart} disabled={startLearn.isPending} className="w-full sm:w-auto">
            {startLearn.isPending && <Icons.loading className="size-4 animate-spin" />}
            Начать тренировку
          </Button>
        </>
      )}
    </div>
  );
}

/**
 * Полоска общего прогресса темы (#568): сколько вопросов охвата в статусе «знаю».
 * Питается тем же списком, что и под-режимы (React Query дедупит ключ); KNOWN —
 * результат карточек/тестов/тренировки. Пусто/ошибка/0 вопросов — не рендерим.
 */
function TopicProgressStrip({ topicId }: { topicId: string }) {
  const listQuery = useQuery(trainerQuestionsQueryOptions.listOptions(topicId));
  if (listQuery.isPending || listQuery.isError) return null;

  const items = listQuery.data.items;
  const total = items.length;
  if (total === 0) return null;

  const known = items.filter((item) => item.status === "KNOWN").length;
  const percent = Math.round((known / total) * 100);
  const tone = getMasteryTone(percent);

  return (
    <div className="flex items-center gap-3 rounded-lg border border-border/60 bg-card/60 px-3.5 py-2.5">
      <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-border/50">
        <div
          className={cn("h-full rounded-full transition-[width] duration-500", tone)}
          style={{ width: `${Math.max(percent, known > 0 ? 4 : 0)}%` }}
        />
      </div>
      <span className="shrink-0 text-xs font-medium tabular-nums text-muted-foreground">
        Освоено {known} из {total}
      </span>
    </div>
  );
}
