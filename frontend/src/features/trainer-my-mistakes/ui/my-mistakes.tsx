"use client";

import {
  trainerQuestionsQueryOptions,
  type TrainerMistakeItem,
  type TrainerMistakesFilter,
} from "@/entities/trainer-question";
import { type TrainerTopicListItem } from "@/entities/trainer-topic";
import { getErrorMessage } from "@/shared/api";
import {
  TRAINER_DIFFICULTIES,
  TRAINER_DIFFICULTY_VISUALS,
  TRAINER_STUDY_STATUS_VISUALS,
} from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { formatRelativeDate } from "@/shared/lib/date/format";
import { pluralize } from "@/shared/lib/pluralize";
import { isTrainerContentRedacted } from "@/shared/lib/trainer-redaction";
import {
  LockedContentPlaceholder,
  SegmentedControl,
  type SegmentedOption,
} from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import type { CSSProperties } from "react";
import { useState } from "react";

interface MyMistakesProps {
  /** Карта topicId → тема (для названия темы в строке). */
  topicMap: Map<string, TrainerTopicListItem>;
  /** Ограничение выбранным треком из хаба. */
  topicIds?: Set<string>;
  /** Запустить ТЕСТ по этим вопросам (хаб стартует review-сессию и навигирует). */
  onLaunchTest: (questionIds: string[]) => void;
  /** Идёт старт сессии — блокируем кнопки. */
  isLaunching?: boolean;
}

/** Пресеты «сколько ошибок проработать»: <total + «Все». Тем меньше 5 — без выбора. */
function buildMistakeChoices(total: number): number[] {
  const presets = [5, 10, 20].filter((count) => count < total);
  return [...presets, total];
}

/**
 * Кросс-тематический список «Мои ошибки» (#568, тест-разворот): вопросы со статусом
 * WRONG/REVIEW + счётчик ошибок + тема + давность. «Доучить» запускает ТЕСТ (а не
 * самопроверку) по выбранному числу ошибок; правильный ответ авто-зачитывает ошибку
 * (тест-путь обновляет study-state). Фильтр сложности. Own-data (auth-gated на бэке).
 */
export function MyMistakes({ topicMap, topicIds, onLaunchTest, isLaunching = false }: MyMistakesProps) {
  const [filter, setFilter] = useState<TrainerMistakesFilter>({});
  const [pickedCount, setPickedCount] = useState<number | null>(null);
  const mistakesQuery = useQuery(trainerQuestionsQueryOptions.mistakesOptions(filter));

  if (mistakesQuery.isPending) {
    return <MistakesSkeleton />;
  }

  if (mistakesQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить ошибки"
        description={getErrorMessage(mistakesQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => mistakesQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const mistakes = topicIds
    ? mistakesQuery.data.filter((item) => topicIds.has(item.topicId))
    : mistakesQuery.data;

  const difficultyOptions: SegmentedOption<string>[] = [
    { value: "ALL", label: "Все" },
    ...TRAINER_DIFFICULTIES.map((level) => ({
      value: level,
      label: TRAINER_DIFFICULTY_VISUALS[level].label,
    })),
  ];

  if (mistakes.length === 0 && !filter.difficulty) {
    return (
      <EmptyState
        icon={Icons.completed}
        variant="card"
        title={topicIds ? "Ошибок в этом треке нет" : "Ошибок нет"}
        description={
          topicIds
            ? "Вопросы появятся здесь после ошибок или вопросов на повтор в выбранном треке."
            : "Когда ответишь неверно в тесте или вопрос подойдёт к повтору — он появится здесь, чтобы пройти его снова."
        }
      />
    );
  }

  const total = mistakes.length;
  const choices = buildMistakeChoices(total);
  const showCountPicker = total >= 5;
  // Дефолт: явный выбор, иначе 10 (если в наборе), иначе «Все».
  const selectedCount =
    pickedCount != null && choices.includes(pickedCount)
      ? pickedCount
      : choices.includes(10)
        ? 10
        : total;
  const launchCount = showCountPicker ? selectedCount : total;

  const launchTest = () => {
    if (isLaunching || total === 0) return;
    onLaunchTest(mistakes.slice(0, launchCount).map((item) => item.questionId));
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex flex-col gap-1.5">
          <span className="text-[11px] font-semibold tracking-[0.14em] text-muted-foreground uppercase">
            Сложность
          </span>
          <SegmentedControl
            ariaLabel="Фильтр ошибок по сложности"
            variant="subtle"
            options={difficultyOptions}
            value={filter.difficulty ?? "ALL"}
            onChange={(value) =>
              setFilter((prev) => ({ ...prev, difficulty: value === "ALL" ? undefined : value }))
            }
          />
        </div>
      </div>

      {total > 0 && (
        <div className="flex flex-col gap-3 rounded-xl border border-border/60 bg-card p-4 sm:flex-row sm:items-end sm:justify-between sm:p-5">
          <div className="space-y-2">
            <p className="text-sm font-medium">Пройти тест по ошибкам</p>
            {showCountPicker ? (
              <div className="flex flex-wrap gap-2">
                {choices.map((count) => (
                  <button
                    key={count}
                    type="button"
                    onClick={() => setPickedCount(count)}
                    aria-pressed={selectedCount === count}
                    className={cn(
                      "min-h-[44px] min-w-[64px] rounded-lg border px-4 text-sm font-medium tabular-nums transition-colors",
                      "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:outline-none",
                      selectedCount === count
                        ? "border-primary bg-primary/10 text-primary"
                        : "border-border/60 bg-card/50 text-foreground/80 hover:bg-accent/40",
                    )}
                  >
                    {count === total ? `Все (${total})` : count}
                  </button>
                ))}
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">
                {total} {pluralize(total, "ошибка", "ошибки", "ошибок")} — тест пройдёт по всем.
              </p>
            )}
          </div>
          <Button onClick={launchTest} disabled={isLaunching} className="w-full sm:w-auto">
            {isLaunching && <Icons.loading className="size-4 animate-spin" />}
            Доучить{showCountPicker ? ` ${launchCount}` : ""}
          </Button>
        </div>
      )}

      {mistakes.length === 0 ? (
        <EmptyState
          icon={Icons.searchEmpty}
          variant="dashed"
          title="Нет ошибок под фильтр"
          description="Сбрось фильтр сложности."
        />
      ) : (
        <ul className="space-y-2">
          {mistakes.map((item, index) => (
            <MistakeRow
              key={item.questionId}
              index={index}
              item={item}
              topicTitle={topicMap.get(item.topicId)?.title}
              isLaunching={isLaunching}
              onStudy={() => onLaunchTest([item.questionId])}
            />
          ))}
        </ul>
      )}
    </div>
  );
}

function MistakeRow({
  item,
  topicTitle,
  onStudy,
  index,
  isLaunching,
}: {
  item: TrainerMistakeItem;
  topicTitle: string | undefined;
  onStudy: () => void;
  index: number;
  isLaunching: boolean;
}) {
  const difficultyVisual = item.difficulty ? TRAINER_DIFFICULTY_VISUALS[item.difficulty] : null;
  const statusVisual = TRAINER_STUDY_STATUS_VISUALS[item.status];

  return (
    <li
      className="t-enter rounded-lg border border-border/60 bg-card px-4 py-3"
      style={{ "--t-i": Math.min(index, 12) } as CSSProperties}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1 space-y-1.5">
          {isTrainerContentRedacted(item.isLocked, item.stem) ? (
            <LockedContentPlaceholder lines={1} />
          ) : (
            <p className="line-clamp-2 text-sm leading-snug">{item.stem}</p>
          )}
          <div className="flex flex-wrap items-center gap-1.5 text-[11px] text-muted-foreground">
            {topicTitle && <span className="font-medium text-foreground/70">{topicTitle}</span>}
            {difficultyVisual && (
              <span
                className={cn(
                  "inline-flex items-center rounded-md px-1.5 py-0.5 font-medium",
                  difficultyVisual.badgeClass,
                )}
              >
                {difficultyVisual.label}
              </span>
            )}
            {statusVisual.chipClass && (
              <span
                className={cn(
                  "inline-flex items-center rounded-md px-1.5 py-0.5 font-medium",
                  statusVisual.chipClass,
                )}
              >
                {statusVisual.label}
              </span>
            )}
            <span>· ошибок: {item.timesWrong}</span>
            <span>· {formatRelativeDate(item.lastSeenAt)}</span>
          </div>
        </div>
        <Button variant="outline" size="sm" className="shrink-0" onClick={onStudy} disabled={isLaunching}>
          Доучить
        </Button>
      </div>
    </li>
  );
}

function MistakesSkeleton() {
  return (
    <div className="space-y-5">
      <Skeleton className="h-11 w-full max-w-md rounded-lg" />
      <div className="space-y-2">
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton key={index} className="h-20 w-full rounded-lg" />
        ))}
      </div>
    </div>
  );
}
