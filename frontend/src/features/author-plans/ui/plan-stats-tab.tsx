"use client";

import {
  planStatsQueryOptions,
  type PlanGrantSource,
  type PlanInviteLinkStatsDto,
  type PlanStatsDto,
  type PlanStatsTimeseriesPointDto,
} from "@/entities/access-plan";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { KpiCard } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

const PERIOD_OPTIONS: Array<{ value: number; label: string }> = [
  { value: 7, label: "7 дней" },
  { value: 30, label: "30 дней" },
  { value: 90, label: "90 дней" },
];

const SOURCE_LABELS: Record<PlanGrantSource, string> = {
  INVITE_LINK: "По ссылке",
  ADMIN_GRANT: "Вручную",
  MIGRATION: "Миграция",
  PURCHASE: "Покупка",
  TRIAL: "Бесплатный план",
  GITHUB_ORG: "GitHub-org",
  TELEGRAM_F1: "Telegram",
  AUTO_FREE: "Авто (FREE)",
};

// Палитра подобрана так, чтобы стак-бар читался в обеих темах. Источники, которые
// не попали сюда, рендерятся серым `var(--muted-foreground)`.
const SOURCE_COLORS: Record<string, string> = {
  INVITE_LINK: "hsl(217 91% 60%)",
  ADMIN_GRANT: "hsl(262 83% 58%)",
  PURCHASE: "hsl(38 92% 50%)",
  TRIAL: "hsl(142 71% 45%)",
  GITHUB_ORG: "hsl(280 70% 55%)",
  TELEGRAM_F1: "hsl(199 89% 48%)",
  AUTO_FREE: "hsl(173 80% 40%)",
  MIGRATION: "hsl(220 9% 46%)",
};

interface Props {
  planId: string;
}

export function PlanStatsTab({ planId }: Props) {
  const [periodDays, setPeriodDays] = useState<number>(30);
  const statsQuery = useQuery(planStatsQueryOptions(planId, periodDays));

  const stats = statsQuery.data;
  const isLoading = statsQuery.isLoading;

  if (statsQuery.isError) {
    return (
      <EmptyState
        variant="card"
        icon={Icons.warning}
        title="Не удалось загрузить статистику"
        description="Проверьте подключение и попробуйте обновить страницу."
      />
    );
  }

  return (
    <div className="space-y-6">
      {/* KPI row 1 — totals по статусу */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <KpiCard
          label="Активных"
          value={formatNumber(stats?.totals.active)}
          hint="Сейчас имеют доступ"
          isLoading={isLoading}
        />
        <KpiCard
          label="Отозваны"
          value={formatNumber(stats?.totals.revoked)}
          isLoading={isLoading}
        />
        <KpiCard
          label="Истекли"
          value={formatNumber(stats?.totals.expired)}
          isLoading={isLoading}
        />
        <KpiCard
          label="Всего выдач"
          value={formatNumber(stats?.totals.total)}
          hint="За всё время"
          isLoading={isLoading}
        />
      </div>

      {/* KPI row 2 — period counters */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <KpiCard
          label="Новых за 7 дней"
          value={formatNumber(stats?.periodCounters.last7Days)}
          isLoading={isLoading}
        />
        <KpiCard
          label="Новых за 30 дней"
          value={formatNumber(stats?.periodCounters.last30Days)}
          isLoading={isLoading}
        />
        <KpiCard
          label="Новых за 90 дней"
          value={formatNumber(stats?.periodCounters.last90Days)}
          isLoading={isLoading}
        />
      </div>

      {/* Chart card */}
      <Card>
        <CardHeader className="flex flex-row items-center justify-between gap-3 space-y-0 pb-2">
          <div className="flex items-center gap-2">
            <Icons.chart className="size-4 text-muted-foreground" />
            <CardTitle className="text-base">Выдачи по дням</CardTitle>
          </div>
          <div className="flex gap-1">
            {PERIOD_OPTIONS.map((opt) => (
              <Button
                key={opt.value}
                size="sm"
                variant={opt.value === periodDays ? "default" : "ghost"}
                onClick={() => setPeriodDays(opt.value)}
                className="h-7 px-2.5 text-xs"
              >
                {opt.label}
              </Button>
            ))}
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : stats && hasAnyPoints(stats.timeseries) ? (
            <GrantsTimeseriesChart timeseries={stats.timeseries} />
          ) : (
            <EmptyState
              variant="plain"
              icon={Icons.chart}
              title="За выбранный период выдач не было"
              description="Поделитесь пригласительной ссылкой или выдайте grant вручную — данные появятся здесь."
            />
          )}
        </CardContent>
      </Card>

      {/* Source breakdown + invite-link table */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-1">
          <CardHeader className="pb-2">
            <CardTitle className="text-base">По источникам</CardTitle>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="space-y-2">
                <Skeleton className="h-6 w-full" />
                <Skeleton className="h-6 w-4/5" />
                <Skeleton className="h-6 w-3/5" />
              </div>
            ) : stats && stats.sourceBreakdown.length > 0 ? (
              <ul className="space-y-1.5">
                {stats.sourceBreakdown.map((row) => (
                  <li
                    key={row.source}
                    className="flex items-center justify-between gap-3 text-sm"
                  >
                    <span className="flex items-center gap-2 truncate">
                      <span
                        className="inline-block size-2.5 shrink-0 rounded-sm"
                        style={{ backgroundColor: colorForSource(row.source) }}
                      />
                      <span className="truncate text-muted-foreground">
                        {SOURCE_LABELS[row.source as PlanGrantSource] ?? row.source}
                      </span>
                    </span>
                    <span className="font-medium tabular-nums">{formatNumber(row.count)}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">Источников пока нет</p>
            )}
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader className="pb-2">
            <CardTitle className="text-base">Пригласительные ссылки</CardTitle>
          </CardHeader>
          <CardContent className="px-0 sm:px-2">
            {isLoading ? (
              <div className="space-y-2 px-4 sm:px-2">
                <Skeleton className="h-10 w-full" />
                <Skeleton className="h-10 w-full" />
              </div>
            ) : stats && stats.inviteLinks.length > 0 ? (
              <InviteLinksTable links={stats.inviteLinks} />
            ) : (
              <p className="px-4 text-sm text-muted-foreground sm:px-2">
                У плана пока нет пригласительных ссылок.
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function GrantsTimeseriesChart({
  timeseries,
}: {
  timeseries: PlanStatsTimeseriesPointDto[];
}) {
  // Собираем уникальный набор источников по всему ряду чтобы recharts отрисовал
  // одинаковый стак на каждой колонке (отсутствующий источник = 0).
  const sources: string[] = [];
  for (const point of timeseries) {
    for (const src of Object.keys(point.bySource)) {
      if (!sources.includes(src)) sources.push(src);
    }
  }

  // Если в periode не было ни одного grant'а — sources пуст; стак-чарту нечего рисовать.
  // Подкладываем синтетический ряд `count`, чтобы ось Y читалась.
  const effectiveSources = sources.length > 0 ? sources : ["count"];

  const data = timeseries.map((p) => {
    const row: Record<string, string | number> = {
      day: formatDayLabel(p.day),
    };
    if (sources.length === 0) {
      row.count = p.count;
    } else {
      for (const src of sources) {
        row[src] = p.bySource[src] ?? 0;
      }
    }
    return row;
  });

  return (
    <div className="h-64 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 8, right: 16, bottom: 8, left: -16 }}>
          <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
          <XAxis
            dataKey="day"
            tick={{ fontSize: 12 }}
            stroke="var(--muted-foreground)"
          />
          <YAxis
            allowDecimals={false}
            tick={{ fontSize: 12 }}
            stroke="var(--muted-foreground)"
          />
          <Tooltip
            contentStyle={{
              backgroundColor: "var(--popover)",
              border: "1px solid var(--border)",
              borderRadius: 6,
              fontSize: 12,
            }}
            formatter={(value, name) => [
              value as number,
              SOURCE_LABELS[String(name) as PlanGrantSource] ?? String(name),
            ]}
          />
          {sources.length > 1 ? (
            <Legend
              wrapperStyle={{ fontSize: 12 }}
              formatter={(value: string) =>
                SOURCE_LABELS[value as PlanGrantSource] ?? value
              }
            />
          ) : null}
          {effectiveSources.map((src) => (
            <Bar
              key={src}
              dataKey={src}
              stackId="sources"
              fill={colorForSource(src)}
              radius={[2, 2, 0, 0]}
            />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function InviteLinksTable({ links }: { links: PlanInviteLinkStatsDto[] }) {
  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Метка / токен</TableHead>
            <TableHead className="text-right">Активаций</TableHead>
            <TableHead className="text-right">Уникальных</TableHead>
            <TableHead className="hidden md:table-cell">Первая</TableHead>
            <TableHead className="hidden md:table-cell">Последняя</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {links.map((link) => (
            <TableRow key={link.inviteLinkId}>
              <TableCell className="space-y-1">
                <div className="flex items-center gap-2">
                  {link.label ? (
                    <span className="font-medium">{link.label}</span>
                  ) : (
                    <span className="text-muted-foreground">Без метки</span>
                  )}
                  {!link.isActive ? (
                    <Badge variant="outline" className="font-normal">
                      Отозвана
                    </Badge>
                  ) : null}
                </div>
                <code className="block truncate font-mono text-xs text-muted-foreground">
                  …{link.token.slice(-10)}
                </code>
              </TableCell>
              <TableCell className="text-right tabular-nums font-medium">
                {formatNumber(link.activationsCount)}
              </TableCell>
              <TableCell className="text-right tabular-nums text-muted-foreground">
                {formatNumber(link.uniqueGrantsCount)}
              </TableCell>
              <TableCell className="hidden md:table-cell text-sm text-muted-foreground">
                {formatDateOrDash(link.firstActivationAt)}
              </TableCell>
              <TableCell className="hidden md:table-cell text-sm text-muted-foreground">
                {formatDateOrDash(link.lastActivationAt)}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function hasAnyPoints(timeseries: PlanStatsDto["timeseries"]): boolean {
  for (const p of timeseries) {
    if (p.count > 0) return true;
  }
  return false;
}

function colorForSource(source: string): string {
  return SOURCE_COLORS[source] ?? "var(--muted-foreground)";
}

function formatNumber(n: number | undefined): string {
  if (n === undefined) return "—";
  return new Intl.NumberFormat("ru").format(n);
}

function formatDayLabel(iso: string): string {
  // backend отдаёт ISO date "YYYY-MM-DD" (DateOnly) — парсим стабильно без TZ-сдвига
  const [y, m, d] = iso.split("-").map((x) => Number.parseInt(x, 10));
  return new Date(y, (m ?? 1) - 1, d ?? 1).toLocaleDateString("ru", {
    day: "2-digit",
    month: "2-digit",
  });
}

function formatDateOrDash(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString("ru-RU");
}
