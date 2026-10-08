"use client";

import { useQuery } from "@tanstack/react-query";

import { trainerTrendsQueryOptions, type TrainerTopicMasteryTrend } from "@/entities/trainer-stats";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";

import { deltaTone, formatDelta } from "../lib/trend";
import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

/**
 * Блок «Динамика mastery» (#681 T6): per-topic освоение СЕГОДНЯ vs ~месяц назад + общая дельта.
 * Самофетч (как `StrengthsWeaknesses`); достигается только для авторизованного юзера. Питается из
 * снапшотов — у ранних пользователей истории ещё нет: показываем дружелюбный плейсхолдер, не ошибку.
 */
export function TrendsPanel({ isAuthenticated }: { isAuthenticated: boolean }) {
  const trendsQuery = useQuery({
    ...trainerTrendsQueryOptions(),
    enabled: isAuthenticated,
  });

  const data = trendsQuery.data;
  const hasComparison = data != null && data.overallDelta != null;

  return (
    <StatSection
      title="Динамика mastery"
      icon={<TrainerStatIcon concept="mastery" className="size-4" />}
      hint={data ? `По сравнению с тем, что было ~${data.comparisonDays} дней назад.` : undefined}
      index={4}
    >
      {trendsQuery.isPending ? (
        <div className="space-y-2">
          <Skeleton className="h-16 w-full rounded-xl" />
          <Skeleton className="h-24 w-full rounded-xl" />
        </div>
      ) : !data || data.topics.length === 0 ? (
        <p className="rounded-xl border border-dashed border-border/60 bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          Пройди несколько тренировок — через месяц здесь появится динамика освоения тем.
        </p>
      ) : (
        <div className="space-y-3">
          {hasComparison ? (
            <div className="flex items-center justify-between gap-3 rounded-xl border bg-card p-3">
              <div>
                <div className="text-2xs font-medium tracking-wide text-muted-foreground uppercase">
                  Общий уровень
                </div>
                <div className="text-2xl font-semibold tabular-nums">
                  {data.overallMasteryNow}%
                </div>
              </div>
              <div className="text-right">
                <DeltaBadge delta={data.overallDelta} />
                <div className="mt-1 text-2xs text-muted-foreground tabular-nums">
                  было {data.overallMasteryThen}%
                </div>
              </div>
            </div>
          ) : (
            <p className="rounded-xl border border-dashed border-border/60 bg-muted/20 px-4 py-3 text-center text-xs text-muted-foreground">
              Копим историю — сравнение с прошлым месяцем появится позже. Пока показываем текущий
              уровень.
            </p>
          )}

          <ul className="divide-y divide-border/60">
            {data.topics.map((topic) => (
              <TopicRow key={topic.topicId} topic={topic} />
            ))}
          </ul>
        </div>
      )}
    </StatSection>
  );
}

function TopicRow({ topic }: { topic: TrainerTopicMasteryTrend }) {
  return (
    <li className="flex items-center justify-between gap-3 py-2">
      <span className="min-w-0 truncate text-sm">{topic.topicTitle}</span>
      <div className="flex shrink-0 items-center gap-2">
        <span className="text-sm font-medium tabular-nums text-muted-foreground">
          {topic.masteryNow}%
        </span>
        <DeltaBadge delta={topic.delta} />
      </div>
    </li>
  );
}

/** Бейдж дельты mastery: ↑ зелёный рост / ↓ красный спад / нейтраль / «новая» (нет истории). */
function DeltaBadge({ delta }: { delta: number | null }) {
  const tone = deltaTone(delta);
  const label = formatDelta(delta);

  if (tone === "none") {
    return (
      <span className="rounded-full bg-muted px-2 py-0.5 text-2xs font-medium text-muted-foreground">
        {label}
      </span>
    );
  }

  return (
    <span
      className={cn(
        "inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-2xs font-medium tabular-nums",
        tone === "up" && "bg-green/10 text-green",
        tone === "down" && "bg-destructive/10 text-destructive",
        tone === "flat" && "bg-muted text-muted-foreground",
      )}
    >
      {tone === "up" ? (
        <Icons.chevronUp className="size-3" aria-hidden />
      ) : tone === "down" ? (
        <Icons.chevronDown className="size-3" aria-hidden />
      ) : null}
      {label}
    </span>
  );
}
