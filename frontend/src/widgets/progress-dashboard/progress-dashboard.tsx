"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { userActivityQueryOptions } from "@/entities/user-activity";
import { MyXpProgressCard } from "@/entities/user-progress";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { ActivityGrid } from "./ui/activity-grid";
import { MyCertificatesSection } from "./ui/my-certificates-section";
import { StreakCard } from "./ui/streak-card";
import { TotalsRow } from "./ui/totals-row";

/**
 * Дашборд страницы «Мой прогресс»: стрик → сетка активности (12 недель) →
 * карточка уровня/XP → итоги за всё время → «Мои сертификаты» (#467, скрыта
 * пока пусто). Аноним видит login-CTA (тот же паттерн, что BookmarksList на /saved).
 */
export function ProgressDashboard() {
  const isAuthenticated = useIsAuthenticated();
  const pathname = usePathname();
  const { data, isLoading, error, refetch } = useQuery({
    ...userActivityQueryOptions.myActivityOptions(),
    enabled: isAuthenticated,
  });

  if (!isAuthenticated) {
    const loginHref = `${routes.login}?${new URLSearchParams({ callbackUrl: pathname ?? "/" }).toString()}`;
    return (
      <Card className="border-dashed">
        <CardContent className="flex flex-col items-center gap-3 py-14 text-center">
          <div className="flex size-12 items-center justify-center rounded-full border border-primary/20 bg-primary/10">
            <Icons.streak className="size-5 text-primary" />
          </div>
          <div className="space-y-1">
            <p className="text-base font-semibold">Прогресс доступен после входа</p>
            <p className="text-sm text-muted-foreground">
              Войдите, чтобы видеть стрик, активность и накопленный опыт
            </p>
          </div>
          <Button asChild>
            <Link href={loginHref}>Войти</Link>
          </Button>
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card>
        <CardContent>
          <ErrorCard error={error} onRetry={() => void refetch()} />
        </CardContent>
      </Card>
    );
  }

  if (isLoading || !data) {
    return <ProgressDashboardSkeleton />;
  }

  return (
    <div className="space-y-6">
      <StreakCard streak={data.streak} />
      <ActivityGrid days={data.days} />
      <MyXpProgressCard />
      <TotalsRow totals={data.totals} />
      <MyCertificatesSection />
    </div>
  );
}

function ProgressDashboardSkeleton() {
  return (
    <div className="space-y-6">
      <Skeleton className="h-24 w-full rounded-xl" />
      <Skeleton className="h-44 w-full rounded-xl" />
      <Skeleton className="h-32 w-full rounded-xl" />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Skeleton className="h-20 rounded-xl" />
        <Skeleton className="h-20 rounded-xl" />
        <Skeleton className="h-20 rounded-xl" />
      </div>
    </div>
  );
}
