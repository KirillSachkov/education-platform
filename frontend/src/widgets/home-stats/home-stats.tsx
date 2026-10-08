"use client";

import { useQuery } from "@tanstack/react-query";
import { userActivityQueryOptions } from "@/entities/user-activity";
import { useIsAuthenticated } from "@/shared/auth";
import { Icons } from "@/shared/ui/icons";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { ActivityHeatmap } from "./ui/activity-heatmap";
import { HomeStreakCard } from "./ui/home-streak-card";
import { KpiTiles } from "./ui/kpi-tiles";
import { TrainerPromoCard } from "./ui/trainer-promo-card";

/**
 * Блок персональной статистики на главной (#684): стрик с недельной полоской →
 * heatmap активности за 12 недель → KPI-плитки (уроки/задания/XP) → промо
 * тренажёра. Данные — единый `GET /progress/my/activity` (стрик считается
 * серверно, UNION xp_awards + material_views). Промо тренажёра показывается
 * всегда (не зависит от загрузки статистики). Аноним блок не видит — на главной
 * он и не рендерится, но гейтим защитно.
 */
export function HomeStats() {
  const isAuthenticated = useIsAuthenticated();
  const { data, isLoading, error, refetch } = useQuery({
    ...userActivityQueryOptions.myActivityOptions(),
    enabled: isAuthenticated,
  });

  if (!isAuthenticated) {
    return null;
  }

  return (
    <section className="space-y-4 sm:space-y-5">
      <div className="flex items-center gap-2">
        <Icons.chart className="size-4 text-muted-foreground" />
        <h2 className="text-base font-semibold sm:text-lg">Ваша активность</h2>
      </div>

      {error ? (
        <Card>
          <CardContent>
            <ErrorCard error={error} onRetry={() => void refetch()} />
          </CardContent>
        </Card>
      ) : isLoading || !data ? (
        <HomeStatsSkeleton />
      ) : (
        <>
          <HomeStreakCard streak={data.streak} days={data.days} />
          <ActivityHeatmap days={data.days} />
          <KpiTiles totals={data.totals} />
        </>
      )}

      <TrainerPromoCard />
    </section>
  );
}

function HomeStatsSkeleton() {
  return (
    <>
      <Skeleton className="h-24 w-full rounded-xl" />
      <Skeleton className="h-44 w-full rounded-xl" />
      <div className="grid grid-cols-3 gap-2 sm:gap-4">
        <Skeleton className="h-20 rounded-xl" />
        <Skeleton className="h-20 rounded-xl" />
        <Skeleton className="h-20 rounded-xl" />
      </div>
    </>
  );
}
