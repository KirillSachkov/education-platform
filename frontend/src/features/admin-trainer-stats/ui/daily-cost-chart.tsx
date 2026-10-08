"use client";

import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import type { AdminAiDailyPoint } from "@/entities/trainer-admin-stats";

const dayFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit" });
const fullDateFormatter = new Intl.DateTimeFormat("ru", {
  day: "2-digit",
  month: "long",
  year: "numeric",
});
const rubFormatter = new Intl.NumberFormat("ru", { maximumFractionDigits: 2 });

interface DailyCostChartProps {
  daily: AdminAiDailyPoint[];
}

/**
 * Дневной ряд AI-стоимости (₽) за окно. Recharts house-style тренажёра
 * (CSS-var заливка, Intl-форматтеры, минимальные оси, кастомный tooltip) —
 * зеркалит `features/trainer-statistics/ui/activity-chart`, но self-contained
 * (FSD запрещает cross-slice import между features).
 */
export function DailyCostChart({ daily }: DailyCostChartProps) {
  const series = daily.map((d) => ({
    iso: d.date,
    day: dayFormatter.format(new Date(d.date)),
    costRub: d.costMicroRub / 1e6,
  }));
  const showEveryN = Math.max(1, Math.ceil(series.length / 8));

  if (series.length === 0) {
    return (
      <p className="py-10 text-center text-sm text-muted-foreground">
        Нет данных за выбранный период.
      </p>
    );
  }

  return (
    <div className="h-56 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -4 }}>
          <defs>
            <linearGradient id="trainerAdminCostFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--primary)" stopOpacity={0.3} />
              <stop offset="100%" stopColor="var(--primary)" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid vertical={false} strokeDasharray="3 3" className="stroke-muted/60" />
          <XAxis
            dataKey="day"
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            interval={showEveryN - 1}
          />
          <YAxis
            allowDecimals
            tick={{ fontSize: 11 }}
            stroke="var(--muted-foreground)"
            axisLine={false}
            tickLine={false}
            width={44}
            tickFormatter={(value: number) => `${rubFormatter.format(value)}₽`}
          />
          <Tooltip
            cursor={{ stroke: "var(--border)", strokeDasharray: "3 3" }}
            contentStyle={{
              backgroundColor: "var(--popover)",
              border: "1px solid var(--border)",
              borderRadius: 8,
              fontSize: 12,
              padding: "8px 10px",
            }}
            labelFormatter={(_label, payload) => {
              const iso = payload?.[0]?.payload?.iso as string | undefined;
              return iso ? fullDateFormatter.format(new Date(iso)) : "";
            }}
            formatter={(value) => [`${rubFormatter.format(Number(value))} ₽`, "Стоимость"]}
          />
          <Area
            type="monotone"
            dataKey="costRub"
            name="costRub"
            stroke="var(--primary)"
            strokeWidth={2}
            fill="url(#trainerAdminCostFill)"
            activeDot={{ r: 4, strokeWidth: 0 }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
