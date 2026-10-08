"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions, type AdminStatsRangeParams } from "@/entities/admin-cross-service";
import { usersQueryOptions } from "@/entities/user";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { KpiCard } from "@/shared/ui/components";
import { RegistrationsChart } from "../registrations-chart";

const numberFormatter = new Intl.NumberFormat("ru");

function formatRubles(cents: number): string {
  return new Intl.NumberFormat("ru", {
    style: "currency",
    currency: "RUB",
    maximumFractionDigits: 0,
  }).format(cents / 100);
}

type Props = {
  range: AdminStatsRangeParams;
};

export function OverviewTab({ range }: Props) {
  const userStatsQuery = useQuery(usersQueryOptions.getAdminStatsOptions(range));
  const accessStatsQuery = useQuery(adminCrossServiceQueryOptions.getAccessStatsOptions(range));
  const progressStatsQuery = useQuery(adminCrossServiceQueryOptions.getProgressStatsOptions(range));

  const userStats = userStatsQuery.data;
  const accessStats = accessStatsQuery.data;
  const progressStats = progressStatsQuery.data;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        <KpiCard
          label="Всего пользователей"
          value={userStats ? numberFormatter.format(userStats.totalUsers) : "—"}
          isLoading={userStatsQuery.isLoading}
        />
        <KpiCard
          label="Новых за период"
          value={userStats ? numberFormatter.format(userStats.newInRange) : "—"}
          isLoading={userStatsQuery.isLoading}
        />
        <KpiCard
          label="Активных учеников"
          value={progressStats ? numberFormatter.format(progressStats.activeUsersInRange) : "—"}
          hint="за выбранный период"
          isLoading={progressStatsQuery.isLoading}
        />
        <KpiCard
          label="Доход"
          value={accessStats ? formatRubles(accessStats.revenueInRangeCents) : "—"}
          hint={
            accessStats
              ? `${numberFormatter.format(accessStats.paidOrdersInRangeCount)} оплаченных`
              : undefined
          }
          isLoading={accessStatsQuery.isLoading}
        />
        <KpiCard
          label="Активных подписок"
          value={accessStats ? numberFormatter.format(accessStats.activeGrantsCount) : "—"}
          hint="всего сейчас"
          isLoading={accessStatsQuery.isLoading}
        />
        <KpiCard
          label="Решений за период"
          value={progressStats ? numberFormatter.format(progressStats.submissionsInRange) : "—"}
          isLoading={progressStatsQuery.isLoading}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="text-base">Регистрации по дням</CardTitle>
          </CardHeader>
          <CardContent>
            {userStats ? (
              <RegistrationsChart data={userStats.dailyRegistrations} />
            ) : (
              <div className="h-64 w-full animate-pulse rounded-md bg-muted/40" />
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">По ролям</CardTitle>
          </CardHeader>
          <CardContent>
            {userStats ? (
              <ul className="space-y-2 text-sm">
                {Object.entries(userStats.byRole).map(([role, count]) => (
                  <li key={role} className="flex items-center justify-between gap-2">
                    <span className="truncate text-muted-foreground">{role}</span>
                    <span className="font-medium tabular-nums">{numberFormatter.format(count)}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <div className="space-y-2">
                {[1, 2, 3].map((i) => (
                  <div key={i} className="h-5 animate-pulse rounded bg-muted/40" />
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
