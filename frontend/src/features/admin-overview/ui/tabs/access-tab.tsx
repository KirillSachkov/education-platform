"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCrossServiceQueryOptions, type AdminStatsRangeParams } from "@/entities/admin-cross-service";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { KpiCard } from "@/shared/ui/components";
import { StatLine } from "../stat-line";

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

export function AccessTab({ range }: Props) {
  const accessStatsQuery = useQuery(adminCrossServiceQueryOptions.getAccessStatsOptions(range));
  const stats = accessStatsQuery.data;
  const isLoading = accessStatsQuery.isLoading;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-5">
        <KpiCard
          label="Доход за период"
          value={stats ? formatRubles(stats.revenueInRangeCents) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Доход всего"
          value={stats ? formatRubles(stats.totalRevenueCents) : "—"}
          isLoading={isLoading}
        />
        <KpiCard
          label="Оплачено заказов"
          value={stats ? numberFormatter.format(stats.paidOrdersInRangeCount) : "—"}
          hint="за период"
          isLoading={isLoading}
        />
        <KpiCard
          label="Неуспешных"
          value={stats ? numberFormatter.format(stats.failedOrdersInRangeCount) : "—"}
          hint="за период"
          isLoading={isLoading}
        />
        <KpiCard
          label="Активных подписок"
          value={stats ? numberFormatter.format(stats.activeGrantsCount) : "—"}
          hint="всего сейчас"
          isLoading={isLoading}
        />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Подписки</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <StatLine
              label="Активных"
              value={stats?.activeGrantsCount}
              isLoading={isLoading}
              hint="сейчас"
            />
            <StatLine
              label="Истёкших"
              value={stats?.expiredGrantsCount}
              isLoading={isLoading}
              hint="всего"
            />
            <StatLine
              label="Отозванных"
              value={stats?.revokedGrantsCount}
              isLoading={isLoading}
              hint="всего"
            />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Заказы</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm">
            <StatLine
              label="Оплачено"
              value={stats?.paidOrdersCount}
              isLoading={isLoading}
              hint="всего"
            />
            <StatLine
              label="Неуспешных"
              value={stats?.failedOrdersCount}
              isLoading={isLoading}
              hint="всего"
            />
            <StatLine
              label="Оплачено за период"
              value={stats?.paidOrdersInRangeCount}
              isLoading={isLoading}
            />
            <StatLine
              label="Неуспешных за период"
              value={stats?.failedOrdersInRangeCount}
              isLoading={isLoading}
            />
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Топ планов по активным подпискам</CardTitle>
          <p className="text-xs text-muted-foreground">Снимок на сейчас, не зависит от выбранного периода.</p>
        </CardHeader>
        <CardContent>
          {stats ? (
            stats.topPlans.length === 0 ? (
              <p className="text-sm text-muted-foreground">Пока никто не оформил подписку.</p>
            ) : (
              <ul className="divide-y divide-border text-sm">
                {stats.topPlans.map((p) => (
                  <li key={p.planId} className="flex items-center justify-between gap-2 py-2">
                    <span className="truncate">{p.displayName ?? p.planId}</span>
                    <span className="font-medium tabular-nums">
                      {numberFormatter.format(p.activeGrantsCount)}
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
  );
}
