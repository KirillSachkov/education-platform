"use client";

import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

import type { TrainerMockTrend } from "@/entities/trainer-stats";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

const dayFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit" });
const fullDateFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "long" });

/**
 * Блок 8 — тренд мок-собесов (#568): динамика баллов по завершённым симуляциям.
 * Сырые AI-списки сильных/слабых тем убраны (#614 H2) — сильные/слабые стороны
 * теперь живут единым блоком на измеренном mastery (`TrainerStrengthsPanels`),
 * чтобы не дублировать шумный сигнал из одного разбора. Виджет не рендерит блок,
 * если завершённых моков нет, но компонент и сам безопасен при пустых данных.
 */
export function MockTrend({ trend }: { trend: TrainerMockTrend }) {
  const series = trend.attempts.map((a, index) => ({
    iso: a.completedAt,
    label: dayFormatter.format(new Date(a.completedAt)),
    score: a.scorePercent,
    index,
  }));

  return (
    <StatSection
      title="Мок-собесы"
      icon={<TrainerStatIcon concept="mock" className="size-4" />}
      index={7}
    >
      {series.length > 0 ? (
        <div className="h-48 w-full">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: -12 }}>
              <CartesianGrid vertical={false} strokeDasharray="3 3" className="stroke-muted/60" />
              <XAxis
                dataKey="label"
                tick={{ fontSize: 11 }}
                stroke="var(--muted-foreground)"
                axisLine={false}
                tickLine={false}
              />
              <YAxis
                domain={[0, 100]}
                tick={{ fontSize: 11 }}
                stroke="var(--muted-foreground)"
                axisLine={false}
                tickLine={false}
                width={32}
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
                formatter={(value) => [`${Number(value)}%`, "Балл"]}
              />
              <Line
                type="monotone"
                dataKey="score"
                stroke="var(--primary)"
                strokeWidth={2}
                dot={{ r: 3, strokeWidth: 0, fill: "var(--primary)" }}
                activeDot={{ r: 5, strokeWidth: 0 }}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <p className="py-6 text-center text-sm text-muted-foreground">
          Пройди мок-собес — здесь появится динамика баллов.
        </p>
      )}
    </StatSection>
  );
}
