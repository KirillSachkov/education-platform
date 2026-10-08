"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions, type AdminStatsRangeParams } from "@/entities/admin-cross-service";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { KpiCard } from "@/shared/ui/components";
import { StatLine } from "../stat-line";

const numberFormatter = new Intl.NumberFormat("ru");

type Props = {
  range: AdminStatsRangeParams;
};

export function LearningTab({ range }: Props) {
  const progressStatsQuery = useQuery(adminCrossServiceQueryOptions.getProgressStatsOptions(range));
  const stats = progressStatsQuery.data;
  const isLoading = progressStatsQuery.isLoading;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-5">
        <KpiCard
          label="Всего записей"
          value={stats ? numberFormatter.format(stats.totalEnrollments) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Активных записей"
          value={stats ? numberFormatter.format(stats.activeEnrollments) : "—"}
          hint="не в архиве"
          isLoading={isLoading}
        />
        <KpiCard
          label="Новых за период"
          value={stats ? numberFormatter.format(stats.newEnrollmentsInRange) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Активных учеников"
          value={stats ? numberFormatter.format(stats.activeUsersInRange) : "—"}
          hint="за период"
          isLoading={isLoading}
        />
        <KpiCard
          label="Решений за период"
          value={stats ? numberFormatter.format(stats.submissionsInRange) : "—"}
          isLoading={isLoading}
        />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Активность за всё время</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <StatLine label="Решений отправлено" value={stats?.totalSubmissions} isLoading={isLoading} />
            <StatLine
              label="Решений за неделю"
              value={stats?.submissionsThisWeek}
              isLoading={isLoading}
            />
            <StatLine
              label="Новых записей за неделю"
              value={stats?.newEnrollmentsThisWeek}
              isLoading={isLoading}
            />
            <StatLine
              label="Активных учеников за неделю"
              value={stats?.activeUsersThisWeek}
              isLoading={isLoading}
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Топ курсов по записям</CardTitle>
            <p className="text-xs text-muted-foreground">Снимок на сейчас, не зависит от выбранного периода.</p>
          </CardHeader>
          <CardContent>
            {stats ? (
              stats.topCourses.length === 0 ? (
                <p className="text-sm text-muted-foreground">Записей пока нет.</p>
              ) : (
                <ul className="divide-y divide-border text-sm">
                  {stats.topCourses.map((c) => (
                    <li key={c.courseId} className="flex items-center justify-between gap-2 py-2">
                      <span className="truncate font-mono text-xs text-muted-foreground">
                        {c.courseId.slice(0, 8)}
                      </span>
                      <span className="font-medium tabular-nums">
                        {numberFormatter.format(c.enrollmentCount)}
                      </span>
                    </li>
                  ))}
                </ul>
              )
            ) : (
              <div className="space-y-2">
                {[1, 2, 3].map((i) => (
                  <div key={i} className="h-6 animate-pulse rounded bg-muted/40" />
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
