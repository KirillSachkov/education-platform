"use client";

import { useQuery } from "@tanstack/react-query";
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

import {
  type AdminNewReturningDay,
  type AdminRetentionBucket,
  type AdminSessionsByModeDay,
  adminTrafficStatsQueryOptions,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { Icons } from "@/shared/ui/icons";

import {
  CHART_TOOLTIP_STYLE,
  dayFormatter,
  everyNTicks,
  isoTooltipLabel,
  SERIES_COLORS,
} from "./chart-common";
import {
  AdminStatCard,
  AdminStatSection,
  EmptyRow,
  int,
  pct,
  StatsErrorState,
  TabSkeleton,
} from "./primitives";

/** Трафик-вкладка (#681 T3): DAU/WAU/MAU + retention + сессии по режиму + новые vs вернувшиеся. */
export function TrafficTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminTrafficStatsQueryOptions(days));

  if (error) return <StatsErrorState error={error} />;
  if (isLoading || !data) return <TabSkeleton />;

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-3 gap-3">
        <AdminStatCard
          label="DAU"
          value={data.activeUsers.dau}
          icon={<Icons.users className="size-4" />}
          footer="за сутки"
          index={0}
        />
        <AdminStatCard
          label="WAU"
          value={data.activeUsers.wau}
          icon={<Icons.users className="size-4" />}
          footer="за 7 дней"
          index={1}
        />
        <AdminStatCard
          label="MAU"
          value={data.activeUsers.mau}
          icon={<Icons.users className="size-4" />}
          footer="за 30 дней"
          index={2}
        />
      </section>

      <AdminStatSection
        title="Удержание (retention)"
        icon={<Icons.streak className="size-4" />}
        hint="Доля новичков окна, вернувшихся через 1 / 7 / 30 дней после первой сессии."
      >
        <div className="grid grid-cols-3 gap-3">
          <RetentionCard label="D1" bucket={data.retention.d1} />
          <RetentionCard label="D7" bucket={data.retention.d7} />
          <RetentionCard label="D30" bucket={data.retention.d30} />
        </div>
      </AdminStatSection>

      <AdminStatSection title="Сессии по режиму, по дням" icon={<Icons.chart className="size-4" />}>
        <SessionsByModeChart rows={data.sessionsByMode} />
      </AdminStatSection>

      <AdminStatSection title="Новые vs вернувшиеся, по дням" icon={<Icons.calendar className="size-4" />}>
        <NewReturningChart rows={data.newVsReturning} />
      </AdminStatSection>
    </div>
  );
}

function RetentionCard({ label, bucket }: { label: string; bucket: AdminRetentionBucket }) {
  return (
    <div className="rounded-xl border bg-card p-3">
      <div className="text-2xs font-medium tracking-wide text-muted-foreground uppercase">
        {label}
      </div>
      <div className="mt-1 text-2xl font-semibold tabular-nums">{pct(bucket.rate)}</div>
      <div className="mt-0.5 text-xs text-muted-foreground tabular-nums">
        {int.format(bucket.returnedCount)} из {int.format(bucket.cohortSize)}
      </div>
    </div>
  );
}

function SessionsByModeChart({ rows }: { rows: AdminSessionsByModeDay[] }) {
  if (rows.length === 0) return <EmptyRow text="Сессий за период не было." />;
  const series = rows.map((r) => ({
    iso: r.date,
    day: dayFormatter.format(new Date(r.date)),
    drill: r.drill,
    learn: r.learn,
    mock: r.mock,
  }));
  const interval = everyNTicks(series.length) - 1;

  return (
    <div className="h-64 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -16 }}>
          <CartesianGrid vertical={false} strokeDasharray="3 3" className="stroke-muted/60" />
          <XAxis
            dataKey="day"
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            interval={interval}
          />
          <YAxis
            allowDecimals={false}
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            width={32}
          />
          <Tooltip
            contentStyle={CHART_TOOLTIP_STYLE}
            cursor={{ fill: "var(--muted)", opacity: 0.4 }}
            labelFormatter={isoTooltipLabel}
            formatter={(value, name) => [
              int.format(Number(value)),
              name === "drill" ? "Тренировка" : name === "learn" ? "Обучение" : "Мок-собес",
            ]}
          />
          <Legend
            formatter={(value) =>
              value === "drill" ? "Тренировка" : value === "learn" ? "Обучение" : "Мок-собес"
            }
            wrapperStyle={{ fontSize: 12 }}
          />
          <Bar dataKey="drill" stackId="m" fill={SERIES_COLORS.primary} radius={[0, 0, 0, 0]} />
          <Bar dataKey="learn" stackId="m" fill={SERIES_COLORS.blue} radius={[0, 0, 0, 0]} />
          <Bar dataKey="mock" stackId="m" fill={SERIES_COLORS.purple} radius={[3, 3, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function NewReturningChart({ rows }: { rows: AdminNewReturningDay[] }) {
  if (rows.length === 0) return <EmptyRow text="Активности за период не было." />;
  const series = rows.map((r) => ({
    iso: r.date,
    day: dayFormatter.format(new Date(r.date)),
    newUsers: r.newUsers,
    returningUsers: r.returningUsers,
  }));
  const interval = everyNTicks(series.length) - 1;

  return (
    <div className="h-64 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -16 }}>
          <CartesianGrid vertical={false} strokeDasharray="3 3" className="stroke-muted/60" />
          <XAxis
            dataKey="day"
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            interval={interval}
          />
          <YAxis
            allowDecimals={false}
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            width={32}
          />
          <Tooltip
            contentStyle={CHART_TOOLTIP_STYLE}
            cursor={{ fill: "var(--muted)", opacity: 0.4 }}
            labelFormatter={isoTooltipLabel}
            formatter={(value, name) => [
              int.format(Number(value)),
              name === "newUsers" ? "Новые" : "Вернувшиеся",
            ]}
          />
          <Legend
            formatter={(value) => (value === "newUsers" ? "Новые" : "Вернувшиеся")}
            wrapperStyle={{ fontSize: 12 }}
          />
          <Bar dataKey="newUsers" stackId="u" fill={SERIES_COLORS.primary} radius={[0, 0, 0, 0]} />
          <Bar
            dataKey="returningUsers"
            stackId="u"
            fill={SERIES_COLORS.muted}
            radius={[3, 3, 0, 0]}
          />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
