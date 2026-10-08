"use client";

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

import type { TrainerActivityDay, TrainerActivityRange } from "@/entities/trainer-stats";
import { SegmentedControl } from "@/shared/ui/components";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

const dayFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit" });
const fullDateFormatter = new Intl.DateTimeFormat("ru", {
  day: "2-digit",
  month: "long",
  year: "numeric",
});

interface ActivityChartProps {
  days: TrainerActivityDay[];
  range: TrainerActivityRange;
  onRangeChange: (range: TrainerActivityRange) => void;
}

/**
 * Блок 3 — график активности по дням (#568): отвечено вопросов (area) + точность
 * (line, %). Переключатель периода 30/90. Recharts house-style (CSS-var заливка,
 * Intl-форматтеры, минимальные оси, кастомный tooltip).
 */
export function ActivityChart({ days, range, onRangeChange }: ActivityChartProps) {
  const series = days.map((d) => ({
    iso: d.date,
    day: dayFormatter.format(new Date(d.date)),
    answered: d.answered,
    accuracy: d.accuracyPercent,
  }));
  const showEveryN = Math.max(1, Math.ceil(series.length / 8));

  return (
    <StatSection
      title="По дням"
      icon={<TrainerStatIcon concept="activity" className="size-4" />}
      index={2}
      action={
        <SegmentedControl
          ariaLabel="Период активности"
          variant="subtle"
          value={String(range)}
          onChange={(v) => onRangeChange(Number(v) as TrainerActivityRange)}
          options={[
            { value: "30", label: "30 дней" },
            { value: "90", label: "90 дней" },
          ]}
        />
      }
    >
      {series.length === 0 ? (
        <p className="py-10 text-center text-sm text-muted-foreground">
          Нет активности за выбранный период.
        </p>
      ) : (
        <div className="h-56 w-full">
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -12 }}>
              <defs>
                <linearGradient id="trainerActivityFill" x1="0" y1="0" x2="0" y2="1">
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
                yAxisId="count"
                allowDecimals={false}
                tick={{ fontSize: 11 }}
                stroke="var(--muted-foreground)"
                axisLine={false}
                tickLine={false}
                width={32}
              />
              <YAxis yAxisId="accuracy" hide domain={[0, 100]} />
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
                formatter={(value, name) =>
                  name === "accuracy"
                    ? [`${Number(value)}%`, "Точность"]
                    : [Number(value), "Отвечено"]
                }
              />
              <Area
                yAxisId="count"
                type="monotone"
                dataKey="answered"
                name="answered"
                stroke="var(--primary)"
                strokeWidth={2}
                fill="url(#trainerActivityFill)"
                activeDot={{ r: 4, strokeWidth: 0 }}
              />
              <Line
                yAxisId="accuracy"
                type="monotone"
                dataKey="accuracy"
                name="accuracy"
                stroke="var(--green)"
                strokeWidth={2}
                dot={false}
                activeDot={{ r: 3, strokeWidth: 0 }}
              />
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      )}
    </StatSection>
  );
}
