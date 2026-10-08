"use client";

import type { CSSProperties } from "react";

import {
  trainerTopicsQueryOptions,
  type TrainerTopicListItem,
} from "@/entities/trainer-topic";
import { getErrorMessage } from "@/shared/api";
import { resolveUnlockHref } from "@/shared/lib/lock-copy";
import { cn } from "@/shared/lib/css";
import { LockCallout, LockIconBadge } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { getTrainerTopicStudyStatus, type TrainerTopicStudyStatusKind } from "../lib/topic-status";

interface HubTopicPickerProps {
  trackId?: string;
  isAuthenticated: boolean;
  /**
   * Полностью скрывать платные (PRO) темы. По умолчанию `false` — locked-темы
   * показываются с замком-бейджем, а клик открывает paywall (фримиум-воронка,
   * #614 B2), а не запускает сессию. `true` оставлено для мест, где замки
   * нежелательны вовсе.
   */
  hideLocked?: boolean;
  /** Заголовок над списком. */
  title: string;
  /** Подсказка под заголовком. */
  hint?: string;
  /** Выбрать тему → родитель переключает под-режим (вопросы / конфиг теста). */
  onPick: (topic: TrainerTopicListItem) => void;
}

/**
 * Пикер темы для режимов «Изучение» и «Тесты» хаба (#568): спокойная сетка
 * карточек-тем выбранного трека. Состояние показывается текстовой меткой и
 * тонкой progress-line, без крупных декоративных элементов.
 */
export function HubTopicPicker({
  trackId,
  isAuthenticated,
  hideLocked = false,
  title,
  hint,
  onPick,
}: HubTopicPickerProps) {
  const topicsQuery = useQuery(
    trainerTopicsQueryOptions.topicsOptions(trackId ? { trackId } : {}),
  );

  if (topicsQuery.isPending) {
    return <TopicPickerSkeleton />;
  }

  if (topicsQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить темы"
        description={getErrorMessage(topicsQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => topicsQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const topics = hideLocked
    ? topicsQuery.data.filter((topic) => !topic.isLocked)
    : topicsQuery.data;

  if (topics.length === 0) {
    return (
      <EmptyState
        icon={Icons.target}
        variant="card"
        title="Тем пока нет"
        description="Выбери другой трек — или загляни позже, темы добавляются."
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h2 className="text-base font-semibold tracking-tight">{title}</h2>
          {hint && <p className="text-sm text-muted-foreground">{hint}</p>}
        </div>
        <span className="rounded-full border border-border/60 bg-card/70 px-3 py-1 text-xs font-medium text-muted-foreground">
          {topics.length} тем
        </span>
      </div>
      <ul className="grid grid-cols-1 gap-3 min-[420px]:grid-cols-2 lg:grid-cols-3">
        {topics.map((topic, index) => (
          <TopicCard
            key={topic.id}
            index={index}
            topic={topic}
            showMastery={isAuthenticated && topic.answersCount > 0}
            onPick={() => onPick(topic)}
          />
        ))}
      </ul>
    </div>
  );
}

function TopicCard({
  topic,
  showMastery,
  onPick,
  index,
}: {
  topic: TrainerTopicListItem;
  showMastery: boolean;
  onPick: () => void;
  index: number;
}) {
  const status = getTrainerTopicStudyStatus({
    masteryPercent: topic.masteryPercent,
    coveragePercent: topic.coveragePercent,
    isWeak: topic.isWeak,
    answersCount: showMastery ? topic.answersCount : 0,
    isLocked: topic.isLocked,
  });
  const tone = getTopicCardTone(status.kind);
  // «Освоение» = покрытие темы (#664), а не EWMA-mastery — честно отражает пройденную долю.
  const safePercent = Math.max(0, Math.min(100, topic.coveragePercent));

  const body = (
    <>
      <span
        aria-hidden="true"
        className={cn(
          "pointer-events-none absolute inset-0 bg-gradient-to-br via-transparent to-transparent",
          tone.tintClass,
        )}
      />

      <div className="flex min-w-0 items-start justify-between gap-3">
        <h3 className="line-clamp-2 min-h-[2.5rem] flex-1 text-sm font-semibold leading-snug tracking-tight">
          {topic.title}
        </h3>
        {topic.isLocked && (
          <LockIconBadge reason={topic.lockReason ?? "pro_required"} variant="inline" />
        )}
      </div>

      <div className="mt-5 space-y-2">
        <div className="flex items-center justify-between gap-3 text-xs">
          <span className="text-muted-foreground">
            {status.hasActivity ? "Освоение" : "Не изучена"}
          </span>
          <span className="font-medium tabular-nums text-muted-foreground">
            {status.hasActivity ? `${safePercent}%` : "0%"}
          </span>
        </div>
        <div className="h-1.5 overflow-hidden rounded-full bg-border/45">
          <div
            className={cn("h-full rounded-full", tone.progressClass)}
            style={{ width: `${status.hasActivity ? Math.max(safePercent, 4) : 0}%` }}
          />
        </div>
      </div>

      <div className="mt-auto flex min-h-8 items-center justify-between gap-3 pt-5">
        <span
          className={cn(
            "min-w-0 truncate rounded-full px-2.5 py-0.5 text-[11px] font-medium",
            tone.badgeClass,
          )}
        >
          {status.label}
        </span>
        <span
          className={cn(
            "inline-flex shrink-0 items-center gap-1 text-xs font-semibold",
            "text-muted-foreground transition-colors group-hover:text-primary",
          )}
        >
          {topic.isLocked ? "По подписке" : showMastery ? "Продолжить" : "Начать"}
          <Icons.chevronRight className="size-3.5" />
        </span>
      </div>
    </>
  );

  const cardClassName = cn(
    "group relative isolate flex h-full min-h-[138px] w-full flex-col overflow-hidden rounded-xl border bg-card/60 p-4 text-left",
    "shadow-[inset_0_1px_0_rgba(255,255,255,0.035)] transition-colors duration-200 hover:bg-card/80",
    tone.border,
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
  );

  // Locked-тема (#614 B2): клик не запускает сессию — открывает paywall-popover
  // «Доступно по подписке Trainer Pro» → /pricing. Мирорит карточки каталога.
  if (topic.isLocked) {
    return (
      <li className="t-enter" style={{ "--t-i": Math.min(index, 12) } as CSSProperties}>
        <Popover>
          <PopoverTrigger asChild>
            <button type="button" title={topic.title} className={cn(cardClassName, "cursor-pointer")}>
              {body}
            </button>
          </PopoverTrigger>
          <PopoverContent
            align="start"
            sideOffset={8}
            collisionPadding={12}
            className="w-[min(20rem,calc(100vw-1.5rem))] rounded-2xl border-border/60 bg-card/95 p-4 shadow-xl shadow-black/20 backdrop-blur-xl"
          >
            <LockCallout
              reason={topic.lockReason ?? "pro_required"}
              ctaHref={resolveUnlockHref({ lockReason: topic.lockReason ?? "pro_required" })}
            />
          </PopoverContent>
        </Popover>
      </li>
    );
  }

  return (
    <li className="t-enter" style={{ "--t-i": Math.min(index, 12) } as CSSProperties}>
      <button type="button" onClick={onPick} title={topic.title} className={cardClassName}>
        {body}
      </button>
    </li>
  );
}

function getTopicCardTone(kind: TrainerTopicStudyStatusKind) {
  if (kind === "locked" || kind === "not-started") {
    return {
      border: "border-border/60 hover:border-primary/20",
      tintClass: "from-transparent",
      progressClass: "bg-border",
      badgeClass: "bg-muted text-muted-foreground",
    };
  }

  if (kind === "needs-work" || kind === "review-errors") {
    return {
      border: "border-border/60 hover:border-amber-500/25",
      tintClass: "from-amber-500/[0.055]",
      progressClass: "bg-amber-500/55",
      badgeClass: "bg-amber-500/10 text-amber-600 dark:text-amber-400",
    };
  }

  if (kind === "strong") {
    return {
      border: "border-border/60 hover:border-green/25",
      tintClass: "from-green/[0.045]",
      progressClass: "bg-green/60",
      badgeClass: "bg-green/10 text-green",
    };
  }

  return {
    border: "border-border/60 hover:border-primary/25",
    tintClass: "from-primary/[0.045]",
    progressClass: "bg-primary/65",
    badgeClass: "bg-primary/10 text-primary",
  };
}

function TopicPickerSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-5 w-40" />
      <div className="grid grid-cols-1 gap-3 min-[420px]:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={index} className="h-36 w-full rounded-xl" />
        ))}
      </div>
    </div>
  );
}
