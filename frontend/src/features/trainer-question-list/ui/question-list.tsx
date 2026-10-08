"use client";

import {
  trainerQuestionsQueryOptions,
  type TrainerQuestionListItem,
  type TrainerQuestionsFilter,
} from "@/entities/trainer-question";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import {
  TRAINER_DIFFICULTIES,
  TRAINER_DIFFICULTY_VISUALS,
  TRAINER_STUDY_STATUS_VISUALS,
} from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { isTrainerContentRedacted } from "@/shared/lib/trainer-redaction";
import { LockCallout, LockedContentPlaceholder } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import type { CSSProperties } from "react";
import { useState } from "react";
import { toast } from "sonner";
import { useToggleQuestionBookmark } from "../model/use-toggle-question-bookmark";

const STATUS_FILTER_OPTIONS = ["NEW", "SEEN", "KNOWN", "REVIEW", "WRONG"] as const;

interface QuestionListProps {
  topicId: string;
  /**
   * Залогинен ли вызывающий. Аноним просматривает метаданные вопросов read-only (#614 F);
   * закладка для анонима ведёт на логин (open-card гейтит виджет через onOpenCard).
   */
  isAuthenticated: boolean;
  /** Открыть карточку вопроса (виджет переключает под-режим на колоду). */
  onOpenCard: (questionId: string) => void;
}

/**
 * Список вопросов охвата темы (#568 Ф2 + design pass 2): кликабельные строки-карточки
 * со статус-маркером (иконкой слева — отличается от уровня-бейджа), стемом, бейджем
 * сложности и кнопкой-закладкой. Фильтры (сложность / статус) — компактные селекты.
 * Клик по строке → карточка изучения. PRO-тема без доступа → lock. Mobile-first.
 */
export function QuestionList({ topicId, isAuthenticated, onOpenCard }: QuestionListProps) {
  const [filter, setFilter] = useState<TrainerQuestionsFilter>({});
  const listQuery = useQuery(trainerQuestionsQueryOptions.listOptions(topicId, filter));

  if (listQuery.isPending) {
    return <QuestionListSkeleton />;
  }

  if (listQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить вопросы"
        description={getErrorMessage(listQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => listQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const { items, isLocked, lockReason } = listQuery.data;

  const patch = (next: Partial<TrainerQuestionsFilter>) =>
    setFilter((prev) => ({ ...prev, ...next }));

  return (
    <div className="space-y-4">
      {isLocked && (
        <div className="rounded-xl border border-border/60 bg-card p-4 sm:p-5">
          <LockCallout reason={lockReason ?? "pro_required"} ctaHref={routes.trainerPro} />
        </div>
      )}

      {/* Фильтры — компактные селекты */}
      <div className="flex flex-wrap items-center gap-3">
        <Select
          value={filter.difficulty ?? "ALL"}
          onValueChange={(value) => patch({ difficulty: value === "ALL" ? undefined : value })}
        >
          <SelectTrigger className="h-9 w-[170px]" aria-label="Фильтр по сложности">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Любая сложность</SelectItem>
            {TRAINER_DIFFICULTIES.map((level) => (
              <SelectItem key={level} value={level}>
                {TRAINER_DIFFICULTY_VISUALS[level].label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={filter.status ?? "ALL"}
          onValueChange={(value) => patch({ status: value === "ALL" ? undefined : value })}
        >
          <SelectTrigger className="h-9 w-[170px]" aria-label="Фильтр по статусу изучения">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Любой статус</SelectItem>
            {STATUS_FILTER_OPTIONS.map((status) => (
              <SelectItem key={status} value={status}>
                {TRAINER_STUDY_STATUS_VISUALS[status].label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {items.length === 0 ? (
        <EmptyState
          icon={Icons.searchEmpty}
          variant="dashed"
          title="Нет вопросов под фильтр"
          description="Сбрось фильтры или выбери другую тему."
        />
      ) : (
        <ul className="space-y-2">
          {items.map((item, index) => (
            <QuestionRow
              key={item.questionId}
              index={index}
              topicId={topicId}
              item={item}
              isAuthenticated={isAuthenticated}
              onOpen={() => onOpenCard(item.questionId)}
            />
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Статус-маркер вопроса — цветной кружок с иконкой (отдельный визуальный канал,
 * НЕ бейдж как у сложности). Switch со статичными `<Icons.x/>` — для
 * react-hooks/static-components. NEW — пунктирный кружок без иконки.
 */
function StatusMarker({ status }: { status: string }) {
  const label = TRAINER_STUDY_STATUS_VISUALS[status]?.label ?? "Новый";
  const base = "flex size-7 shrink-0 items-center justify-center rounded-full";
  switch (status) {
    case "KNOWN":
      return (
        <span className={cn(base, "bg-green/12")} title={label}>
          <Icons.check className="size-4 text-green" />
        </span>
      );
    case "REVIEW":
      return (
        <span className={cn(base, "bg-amber-500/12")} title={label}>
          <Icons.restore className="size-4 text-amber-600 dark:text-amber-400" />
        </span>
      );
    case "WRONG":
      return (
        <span className={cn(base, "bg-destructive/12")} title={label}>
          <Icons.close className="size-4 text-destructive" />
        </span>
      );
    case "SEEN":
      return (
        <span className={cn(base, "bg-muted")} title={label}>
          <Icons.view className="size-4 text-muted-foreground" />
        </span>
      );
    default:
      return (
        <span
          className={cn(base, "border border-dashed border-muted-foreground/40")}
          title="Новый — ещё не изучен"
          aria-hidden="true"
        />
      );
  }
}

/**
 * Превью стема для строки списка: если есть вставка кода (```), обрезаем по неё и
 * добавляем «…»; инлайн-бэктики `code` снимаем. Иначе сырой markdown ломает строку.
 */
function previewStem(stem: string): string {
  const fenceIdx = stem.indexOf("```");
  const head = fenceIdx >= 0 ? stem.slice(0, fenceIdx) : stem;
  const cleaned = head
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\s+/g, " ")
    .trim();
  if (cleaned) return fenceIdx >= 0 ? `${cleaned} …` : cleaned;
  // Стем начинается с кода — показываем без тройных бэктиков.
  return stem
    .replace(/```[a-z]*\n?/gi, "")
    .replace(/`/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

function QuestionRow({
  topicId,
  item,
  isAuthenticated,
  onOpen,
  index,
}: {
  topicId: string;
  item: TrainerQuestionListItem;
  isAuthenticated: boolean;
  onOpen: () => void;
  index: number;
}) {
  const router = useRouter();
  const isLocked = item.isLocked;
  const difficultyVisual = item.difficulty ? TRAINER_DIFFICULTY_VISUALS[item.difficulty] : null;
  const [bookmarked, setBookmarked] = useState(item.isBookmarked);
  const toggle = useToggleQuestionBookmark();

  const handleBookmark = () => {
    // Аноним: закладка требует входа (#614 F) — подсказка + редирект, мутацию не зовём.
    if (!isAuthenticated) {
      toast.info("Войдите, чтобы сохранять вопросы");
      router.push(`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`);
      return;
    }
    if (toggle.isPending) return;
    const next = !bookmarked;
    setBookmarked(next);
    toggle.mutate(
      { topicId, questionId: item.questionId, isBookmarked: bookmarked },
      { onError: () => setBookmarked(!next) },
    );
  };

  return (
    <li className="t-enter" style={{ "--t-i": Math.min(index, 12) } as CSSProperties}>
      <div
        className={cn(
          "group flex items-center rounded-xl border border-border/60 bg-card pr-2",
          // Только цветовые transition — БЕЗ transform (давал мерцание на ховере, #585).
          "transition-colors duration-200",
          "hover:border-border hover:bg-accent/20",
        )}
      >
        <button
          type="button"
          onClick={isLocked ? () => router.push(routes.trainerPro) : onOpen}
          aria-label={
            isLocked ? "Доступно в Тренажёр Pro — оформить подписку" : undefined
          }
          className="flex min-h-[56px] min-w-0 flex-1 items-center gap-3 rounded-xl py-3 pl-3 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
        >
          {isLocked ? (
            <span
              className="flex size-7 shrink-0 items-center justify-center rounded-full bg-violet-500/12"
              title="Доступно в Тренажёр Pro"
            >
              <Icons.locked className="size-3.5 text-violet-500 dark:text-violet-300" />
            </span>
          ) : (
            <StatusMarker status={item.status} />
          )}
          {isTrainerContentRedacted(isLocked, item.stem) ? (
            <LockedContentPlaceholder lines={1} className="min-w-0 flex-1" />
          ) : (
            <span
              className={cn(
                "line-clamp-2 min-w-0 flex-1 text-sm leading-snug",
                isLocked && "text-muted-foreground",
              )}
            >
              {previewStem(item.stem as string)}
            </span>
          )}
          {difficultyVisual && (
            <span
              className={cn(
                "inline-flex shrink-0 items-center rounded-md px-1.5 py-0.5 text-[11px] font-medium",
                difficultyVisual.badgeClass,
              )}
            >
              {difficultyVisual.label}
            </span>
          )}
          {isLocked ? (
            <span className="inline-flex shrink-0 items-center gap-1 rounded-md bg-violet-500/12 px-1.5 py-0.5 text-[11px] font-semibold text-violet-600 dark:text-violet-300">
              <Icons.energy className="size-3" />
              PRO
            </span>
          ) : (
            <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/50" />
          )}
        </button>
        <button
          type="button"
          onClick={handleBookmark}
          disabled={toggle.isPending}
          aria-pressed={bookmarked}
          aria-label={bookmarked ? "Убрать из закладок" : "Добавить в закладки"}
          className="ml-1 flex size-9 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
        >
          {bookmarked ? (
            <Icons.bookmarkFilled className="size-4 text-primary" />
          ) : (
            <Icons.bookmark className="size-4" />
          )}
        </button>
      </div>
    </li>
  );
}

function QuestionListSkeleton() {
  return (
    <div className="space-y-4">
      <div className="flex gap-3">
        <Skeleton className="h-9 w-[170px] rounded-md" />
        <Skeleton className="h-9 w-[170px] rounded-md" />
      </div>
      <div className="space-y-2">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={index} className="h-14 w-full rounded-xl" />
        ))}
      </div>
    </div>
  );
}
