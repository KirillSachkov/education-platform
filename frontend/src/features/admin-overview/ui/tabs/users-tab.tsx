"use client";

import { useQuery } from "@tanstack/react-query";
import { usersQueryOptions, type AdminStatsRange } from "@/entities/user";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { KpiCard } from "@/shared/ui/components";
import { RegistrationsChart } from "../registrations-chart";

const numberFormatter = new Intl.NumberFormat("ru");

type Props = {
  range: AdminStatsRange;
};

export function UsersTab({ range }: Props) {
  const userStatsQuery = useQuery(usersQueryOptions.getAdminStatsOptions(range));
  const userStats = userStatsQuery.data;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-5">
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
          label="Новых сегодня"
          value={userStats ? numberFormatter.format(userStats.newToday) : "—"}
          isLoading={userStatsQuery.isLoading}
        />
        <KpiCard
          label="Подтверждён email"
          value={userStats ? numberFormatter.format(userStats.confirmedEmailCount) : "—"}
          hint="всего"
          isLoading={userStatsQuery.isLoading}
        />
        <KpiCard
          label="Заблокировано"
          value={userStats ? numberFormatter.format(userStats.lockedCount) : "—"}
          isLoading={userStatsQuery.isLoading}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="text-base">Регистрации по дням</CardTitle>
          </CardHeader>
          <CardContent>
            {userStats ? (
              <RegistrationsChart data={userStats.dailyRegistrations} height={320} />
            ) : (
              <div className="h-80 w-full animate-pulse rounded-md bg-muted/40" />
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Распределение по ролям</CardTitle>
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
                {[1, 2, 3, 4].map((i) => (
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
