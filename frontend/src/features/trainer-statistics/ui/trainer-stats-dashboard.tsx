"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";

import {
  trainerActivityQueryOptions,
  trainerMockTrendQueryOptions,
  trainerStatsSummaryQueryOptions,
  type TrainerActivity,
  type TrainerActivityRange,
  type TrainerMockTrend,
  type TrainerStatsSummary,
} from "@/entities/trainer-stats";
import {
  computeInterviewReadiness,
  trainerTopicsQueryOptions,
  type TrainerTopicMastery,
} from "@/entities/trainer-topic";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";

import { ActivityChart } from "./activity-chart";
import { ActivityHeatmap } from "./activity-heatmap";
import { CoverageDonut } from "./coverage-donut";
import { DifficultyAccuracy } from "./difficulty-accuracy";
import { MockTrend } from "./mock-trend";
import { SrsForecast } from "./srs-forecast";
import { StrengthsWeaknesses } from "./strengths-weaknesses";
import { SummaryHeader } from "./summary-header";
import { TrendsPanel } from "./trends-panel";

/**
 * Дашборд статистики тренажёра (#568). Встраивается ВКЛАДКОЙ «Статистика» внутрь
 * хаба `/trainer` (не отдельный роут — по решению владельца статистика живёт в
 * рамках тренажёра как смена вкладки). Композит-слот: тянет прогресс / сводку /
 * активность / тренд моков, отдаёт блоки этого же слайса. Для анона —
 * приглашение войти (auth-gated данные не дёргаются → нет 401).
 */
export function TrainerStatsDashboard({ isAuthenticated }: { isAuthenticated: boolean }) {
  const [range, setRange] = useState<TrainerActivityRange>(90);

  const progressQuery = useQuery({
    ...trainerTopicsQueryOptions.progressOptions(),
    enabled: isAuthenticated,
  });
  // Полный список опубликованных тем — знаменатель охвата для честной «готовности» (#691).
  // Публичный + кэшируется (хаб уже тянет тот же ключ), поэтому обычно отдаётся из кэша.
  const topicsQuery = useQuery({
    ...trainerTopicsQueryOptions.topicsOptions(),
    enabled: isAuthenticated,
  });
  const summaryQuery = useQuery({
    ...trainerStatsSummaryQueryOptions(),
    enabled: isAuthenticated,
  });
  const activityQuery = useQuery({
    ...trainerActivityQueryOptions(range),
    enabled: isAuthenticated,
  });
  const mockTrendQuery = useQuery({
    ...trainerMockTrendQueryOptions(),
    enabled: isAuthenticated,
  });

  const coreError = progressQuery.error ?? summaryQuery.error ?? activityQuery.error;

  if (!isAuthenticated) {
    return (
      <EmptyState
        icon={Icons.trending}
        variant="card"
        title="Статистика после входа"
        description="Войди, чтобы видеть прогресс, активность и слабые места по своей подготовке."
        action={
          <Button asChild>
            <Link href={`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`}>
              Войти
            </Link>
          </Button>
        }
      />
    );
  }

  if (
    progressQuery.isPending ||
    summaryQuery.isPending ||
    activityQuery.isPending ||
    topicsQuery.isPending
  ) {
    return <DashboardSkeleton />;
  }

  if (coreError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        title="Не удалось загрузить статистику"
        description={getErrorMessage(coreError, "Попробуйте обновить страницу")}
        action={
          <Button
            variant="outline"
            onClick={() => {
              progressQuery.refetch();
              summaryQuery.refetch();
              activityQuery.refetch();
            }}
          >
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  return (
    <DashboardContent
      mastery={progressQuery.data?.mastery ?? []}
      totalPublishedTopics={topicsQuery.data?.length ?? 0}
      summary={summaryQuery.data}
      activity={activityQuery.data}
      mockTrend={mockTrendQuery.data}
      range={range}
      onRangeChange={setRange}
    />
  );
}

function DashboardContent({
  mastery,
  totalPublishedTopics,
  summary,
  activity,
  mockTrend,
  range,
  onRangeChange,
}: {
  mastery: TrainerTopicMastery[];
  totalPublishedTopics: number;
  summary: TrainerStatsSummary | undefined;
  activity: TrainerActivity | undefined;
  mockTrend: TrainerMockTrend | undefined;
  range: TrainerActivityRange;
  onRangeChange: (range: TrainerActivityRange) => void;
}) {
  // summary/activity гарантированы (не в загрузке/ошибке к этому моменту).
  if (!summary || !activity) return null;

  // Готовность к собесу (#691): учитывает И глубину (coveragePercent), И охват — нетронутые из
  // ВСЕХ опубликованных тем считаются за 0, поэтому 4 темы из 20 больше не дают обманчивые 75%.
  // Малая выборка → isConfident=false: hero приглушит число вместо уверенного крупного процента.
  const readiness = computeInterviewReadiness(mastery, totalPublishedTopics);
  const weakCount = mastery.filter((row) => row.answersCount > 0 && row.isWeak).length;

  const isEmpty =
    mastery.length === 0 && summary.totalAnswered === 0 && activity.days.length === 0;

  if (isEmpty) {
    return (
      <EmptyState
        icon={Icons.target}
        variant="card"
        title="Пока нет данных"
        description="Пройди первую тренировку — здесь появятся графики прогресса и активности."
        action={
          <Button asChild>
            <Link href={routes.trainer}>
              <Icons.target className="size-4" />К темам
            </Link>
          </Button>
        }
      />
    );
  }

  const hasMock = mockTrend != null && mockTrend.attempts.length > 0;

  return (
    <div className="space-y-4">
      <SummaryHeader
        readinessPercent={readiness.percent}
        touchedTopics={readiness.touchedTopics}
        totalTopics={readiness.totalTopics}
        isConfident={readiness.isConfident}
        weakCount={weakCount}
        currentStreak={activity.currentStreak}
        longestStreak={activity.longestStreak}
        totalAnswered={summary.totalAnswered}
        accuracyPercent={summary.allTimeAccuracyPercent}
        dueToday={summary.srs.dueToday}
        studiedQuestions={summary.studiedQuestions}
      />

      <ActivityHeatmap
        days={activity.days}
        currentStreak={activity.currentStreak}
        longestStreak={activity.longestStreak}
      />

      <ActivityChart days={activity.days} range={range} onRangeChange={onRangeChange} />

      {/* Источник — единый /trainer/stats/strengths (тот же, что на вкладке «Симуляция»),
          самофетч внутри; дашборд достигается только для авторизованного юзера. */}
      <StrengthsWeaknesses isAuthenticated />

      {/* Динамика mastery «vs месяц назад» (#681 T6) — самофетч, дружелюбный плейсхолдер
          для ранних юзеров без истории. */}
      <TrendsPanel isAuthenticated />

      <div className="grid items-start gap-4 lg:grid-cols-2">
        <CoverageDonut
          breakdown={summary.studyStatusBreakdown}
          studiedQuestions={summary.studiedQuestions}
        />
        <DifficultyAccuracy items={summary.difficultyAccuracy} />
      </div>

      <SrsForecast srs={summary.srs} />

      {hasMock ? <MockTrend trend={mockTrend} /> : null}
    </div>
  );
}

function DashboardSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-36 w-full rounded-xl" />
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 w-full rounded-xl" />
        ))}
      </div>
      <Skeleton className="h-44 w-full rounded-xl" />
      <Skeleton className="h-64 w-full rounded-xl" />
    </div>
  );
}
