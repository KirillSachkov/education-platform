"use client";

import type { DailyRegistration } from "@/entities/user";
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

type RegistrationsChartProps = {
  data: DailyRegistration[];
  height?: number;
};

const dayFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit" });
const fullDateFormatter = new Intl.DateTimeFormat("ru", {
  day: "2-digit",
  month: "long",
  year: "numeric",
});
const numberFormatter = new Intl.NumberFormat("ru");

export function RegistrationsChart({ data, height = 256 }: RegistrationsChartProps) {
  const series = data.map((p) => ({
    isoDay: p.day,
    day: dayFormatter.format(new Date(p.day)),
    count: p.count,
  }));

  const showEveryN = Math.max(1, Math.ceil(series.length / 10));

  return (
    <div className="w-full" style={{ height }}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          <defs>
            <linearGradient id="registrationsFill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--primary)" stopOpacity={0.32} />
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
            allowDecimals={false}
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
              const iso = payload?.[0]?.payload?.isoDay as string | undefined;
              return iso ? fullDateFormatter.format(new Date(iso)) : "";
            }}
            formatter={(value) => [
              numberFormatter.format(Number(value)),
              "Регистраций",
            ]}
          />
          <Area
            type="monotone"
            dataKey="count"
            stroke="var(--primary)"
            strokeWidth={2}
            fill="url(#registrationsFill)"
            activeDot={{ r: 4, strokeWidth: 0 }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
