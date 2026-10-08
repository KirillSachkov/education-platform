"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import {
  type AdminTrendPoint,
  adminTrendStatsQueryOptions,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { SegmentedControl } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";

import {
  CHART_CURSOR,
  CHART_TOOLTIP_STYLE,
  dayFormatter,
  everyNTicks,
  isoTooltipLabel,
} from "./chart-common";
import {
  AdminStatCard,
  AdminStatSection,
  EmptyRow,
  int,
  rub,
  StatsErrorState,
  TabSkeleton,
} from "./primitives";

type MetricKey = "sessionsStarted" | "activeUsers" | "completedSessions" | "costRub" | "avgAccuracyPct";

interface MetricMeta {
  key: MetricKey;
  label: string;
  color: string;
  decimals: number;
  suffix?: string;
  format: (n: number) => string;
}

const METRICS: MetricMeta[] = [
  { key: "sessionsStarted", label: "Сессии", color: "var(--primary)", decimals: 0, format: (n) => int.format(n) },
  { key: "activeUsers", label: "Активные", color: "var(--blue)", decimals: 0, format: (n) => int.format(n) },
  { key: "completedSessions", label: "Завершено", color: "var(--purple)", decimals: 0, format: (n) => int.format(n) },
  { key: "costRub", label: "Стоимость ₽", color: "var(--green)", decimals: 2, format: (n) => rub.format(n) },
  { key: "avgAccuracyPct", label: "Точность %", color: "var(--green)", decimals: 0, suffix: "%", format: (n) => `${Math.round(n)}` },
];

/** Тренды (#681 T6): плотная owner-кривая по дням — переключатель метрики + итоги за окно. */
export function TrendsTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminTrendStatsQueryOptions(days));
  const [metric, setMetric] = useState<MetricKey>("sessionsStarted");

  if (error) return <StatsErrorState error={error} />;
  if (isLoading || !data) return <TabSkeleton />;

  const points = data.points;
  const totals = points.reduce(
    (acc, p) => ({
      sessions: acc.sessions + p.sessionsStarted,
      completed: acc.completed + p.completedSessions,
      costRub: acc.costRub + p.costRub,
      accuracySum: acc.accuracySum + p.avgAccuracyPct,
    }),
    { sessions: 0, completed: 0, costRub: 0, accuracySum: 0 },
  );
  const avgAccuracy = points.length > 0 ? totals.accuracySum / points.length : 0;
  const meta = METRICS.find((m) => m.key === metric)!;

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <AdminStatCard label="Сессий за окно" value={totals.sessions} icon={<Icons.chart className="size-4" />} index={0} />
        <AdminStatCard label="Завершено" value={totals.completed} icon={<Icons.check className="size-4" />} index={1} />
        <AdminStatCard
          label="Стоимость, ₽"
          value={totals.costRub}
          format={(n) => rub.format(n)}
          decimals={2}
          icon={<Icons.creditCard className="size-4" />}
          index={2}
        />
        <AdminStatCard
          label="Ср. точность"
          value={avgAccuracy}
          suffix="%"
          icon={<Icons.target className="size-4" />}
          index={3}
        />
      </section>

      <AdminStatSection
        title="Динамика по дням"
        icon={<Icons.trending className="size-4" />}
        action={
          <SegmentedControl
            ariaLabel="Метрика тренда"
            variant="subtle"
            value={metric}
            onChange={setMetric}
            options={METRICS.map((m) => ({ value: m.key, label: m.label }))}
          />
        }
      >
        <TrendChart points={points} meta={meta} />
      </AdminStatSection>
    </div>
  );
}

function TrendChart({ points, meta }: { points: AdminTrendPoint[]; meta: MetricMeta }) {
  if (points.length === 0) return <EmptyRow text="Нет данных за выбранный период." />;
  const series = points.map((p) => ({
    iso: p.date,
    day: dayFormatter.format(new Date(p.date)),
    value: p[meta.key],
  }));
  const interval = everyNTicks(series.length) - 1;

  return (
    <div className="h-64 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -8 }}>
          <defs>
            <linearGradient id="trainerTrendFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={meta.color} stopOpacity={0.3} />
              <stop offset="100%" stopColor={meta.color} stopOpacity={0} />
            </linearGradient>
          </defs>
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
            allowDecimals={meta.decimals > 0}
            domain={meta.key === "avgAccuracyPct" ? [0, 100] : undefined}
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            width={40}
            tickFormatter={(v: number) => meta.format(v)}
          />
          <Tooltip
            cursor={CHART_CURSOR}
            contentStyle={CHART_TOOLTIP_STYLE}
            labelFormatter={isoTooltipLabel}
            formatter={(value) => [`${meta.format(Number(value))}${meta.suffix ?? ""}`, meta.label]}
          />
          <Area
            type="monotone"
            dataKey="value"
            name={meta.key}
            stroke={meta.color}
            strokeWidth={2}
            fill="url(#trainerTrendFill)"
            activeDot={{ r: 4, strokeWidth: 0 }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
