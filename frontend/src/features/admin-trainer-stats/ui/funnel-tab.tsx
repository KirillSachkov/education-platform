"use client";

import { useQuery } from "@tanstack/react-query";
import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import {
  type AdminAbandonedMocks,
  type AdminDropOffPosition,
  type AdminModeCompletion,
  adminFunnelStatsQueryOptions,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { cn } from "@/shared/lib/css";
import { CircularProgress } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";

import { CHART_CURSOR, CHART_TOOLTIP_STYLE, SERIES_COLORS } from "./chart-common";
import {
  AdminStatSection,
  EmptyRow,
  int,
  MODE_LABELS,
  pct,
  StatsErrorState,
  TabSkeleton,
} from "./primitives";

/** Воронка-вкладка (#681 T3): завершаемость (всего + по режиму) + брошенные моки + спад по позиции. */
export function FunnelTab({ days }: { days: TrainerAdminStatsRange }) {
  const { data, isLoading, error } = useQuery(adminFunnelStatsQueryOptions(days));

  if (error) return <StatsErrorState error={error} />;
  if (isLoading || !data) return <TabSkeleton />;

  return (
    <div className="space-y-4">
      <AdminStatSection
        title="Завершаемость сессий"
        icon={<Icons.target className="size-4" />}
        hint="Доля начатых сессий, доведённых до конца (status=COMPLETED)."
      >
        <div className="flex flex-col items-center gap-5 sm:flex-row sm:items-center">
          <div className="relative shrink-0">
            <CircularProgress value={data.completion.rate * 100} size={132} strokeWidth={10} />
            <div className="absolute inset-0 flex flex-col items-center justify-center">
              <span className="text-3xl font-semibold tabular-nums">{pct(data.completion.rate)}</span>
              <span className="text-2xs text-muted-foreground">завершено</span>
            </div>
          </div>
          <div className="grid w-full grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="rounded-xl border bg-card p-3">
              <div className="text-2xs font-medium tracking-wide text-muted-foreground uppercase">
                Начато
              </div>
              <div className="mt-1 text-2xl font-semibold tabular-nums">
                {int.format(data.completion.started)}
              </div>
            </div>
            <div className="rounded-xl border bg-card p-3">
              <div className="text-2xs font-medium tracking-wide text-muted-foreground uppercase">
                Завершено
              </div>
              <div className="mt-1 text-2xl font-semibold tabular-nums">
                {int.format(data.completion.completed)}
              </div>
            </div>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
          {data.completionByMode.map((m) => (
            <ModeCompletionCard key={m.mode} row={m} />
          ))}
        </div>

        <AbandonedMocksRow mocks={data.abandonedMocks} />
      </AdminStatSection>

      <AdminStatSection
        title="Спад по позиции вопроса"
        icon={<Icons.chart className="size-4" />}
        hint="На какой ординальной позиции вопроса пользователи перестают отвечать."
      >
        <DropOffChart rows={data.dropOffByPosition} />
      </AdminStatSection>
    </div>
  );
}

function ModeCompletionCard({ row }: { row: AdminModeCompletion }) {
  return (
    <div className="rounded-xl border bg-card p-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">{MODE_LABELS[row.mode] ?? row.mode}</span>
        <span className="text-sm font-semibold tabular-nums">{pct(row.rate)}</span>
      </div>
      <div className="mt-1 text-xs text-muted-foreground tabular-nums">
        {int.format(row.completed)} из {int.format(row.started)} завершено
      </div>
    </div>
  );
}

function AbandonedMocksRow({ mocks }: { mocks: AdminAbandonedMocks }) {
  const high = mocks.abandonRate >= 0.5 && mocks.mockStarted > 0;
  return (
    <div className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-xl border bg-card p-3">
      <div className="flex items-center gap-2 text-sm">
        <Icons.briefcase className="size-4 text-muted-foreground" aria-hidden />
        <span>Брошенные мок-собесы</span>
      </div>
      <div className="flex items-center gap-3 text-sm">
        <span className="text-muted-foreground tabular-nums">
          {int.format(mocks.mockAbandoned)} из {int.format(mocks.mockStarted)}
        </span>
        <span
          className={cn(
            "font-semibold tabular-nums",
            high ? "text-destructive" : "text-foreground",
          )}
        >
          {pct(mocks.abandonRate)}
        </span>
      </div>
    </div>
  );
}

function DropOffChart({ rows }: { rows: AdminDropOffPosition[] }) {
  if (rows.length === 0) return <EmptyRow text="Нет данных по позициям за период." />;
  const series = rows.map((r) => ({
    position: `#${r.position + 1}`,
    reached: r.reached,
    answered: r.answered,
    answeredRate: Math.round(r.answeredRate * 100),
  }));
  const interval = Math.max(0, Math.ceil(series.length / 12) - 1);

  return (
    <div className="h-64 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -16 }}>
          <defs>
            <linearGradient id="trainerDropOffFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--primary)" stopOpacity={0.3} />
              <stop offset="100%" stopColor="var(--primary)" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid vertical={false} strokeDasharray="3 3" className="stroke-muted/60" />
          <XAxis
            dataKey="position"
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            interval={interval}
          />
          <YAxis
            yAxisId="count"
            allowDecimals={false}
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            width={32}
          />
          <YAxis yAxisId="rate" hide domain={[0, 100]} />
          <Tooltip
            cursor={CHART_CURSOR}
            contentStyle={CHART_TOOLTIP_STYLE}
            labelFormatter={(label) => `Позиция ${label}`}
            formatter={(value, name) =>
              name === "answeredRate"
                ? [`${Number(value)}%`, "Доля ответивших"]
                : [int.format(Number(value)), name === "reached" ? "Дошло" : "Ответило"]
            }
          />
          <Area
            yAxisId="count"
            type="monotone"
            dataKey="reached"
            name="reached"
            stroke="var(--muted-foreground)"
            strokeWidth={1.5}
            fillOpacity={0}
            dot={false}
          />
          <Area
            yAxisId="count"
            type="monotone"
            dataKey="answered"
            name="answered"
            stroke="var(--primary)"
            strokeWidth={2}
            fill="url(#trainerDropOffFill)"
            activeDot={{ r: 4, strokeWidth: 0 }}
          />
          <Line
            yAxisId="rate"
            type="monotone"
            dataKey="answeredRate"
            name="answeredRate"
            stroke={SERIES_COLORS.green}
            strokeWidth={2}
            dot={false}
            activeDot={{ r: 3, strokeWidth: 0 }}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
