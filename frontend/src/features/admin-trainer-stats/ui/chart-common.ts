// Shared recharts house-style bits for admin trainer-stats charts (#681): CSS-var colours,
// Intl date formatters, common tooltip style. Mirrors the daily-cost-chart conventions so all
// admin charts look identical.

import type { CSSProperties } from "react";

export const dayFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit" });
export const fullDateFormatter = new Intl.DateTimeFormat("ru", {
  day: "2-digit",
  month: "long",
  year: "numeric",
});

/** Кастомный фон tooltip'а (popover-токены платформы). */
export const CHART_TOOLTIP_STYLE: CSSProperties = {
  backgroundColor: "var(--popover)",
  border: "1px solid var(--border)",
  borderRadius: 8,
  fontSize: 12,
  padding: "8px 10px",
};

/** Tooltip cursor для линий/областей. */
export const CHART_CURSOR = { stroke: "var(--border)", strokeDasharray: "3 3" } as const;

/** Серийные цвета (concrete CSS-var значения — recharts требует не-классы). */
export const SERIES_COLORS = {
  primary: "var(--primary)",
  blue: "var(--blue)",
  purple: "var(--purple)",
  green: "var(--green)",
  muted: "var(--muted-foreground)",
} as const;

/** Прореживание тиков оси X для плотного дневного ряда (≈8 подписей). */
export const everyNTicks = (length: number) => Math.max(1, Math.ceil(length / 8));

/**
 * labelFormatter для дневных графиков: достаёт ISO-дату из payload и форматирует «01 января 2026».
 * Recharts payload типизирован слабо — берём `iso` из первой точки.
 */
export function isoTooltipLabel(
  _label: unknown,
  payload: ReadonlyArray<{ payload?: { iso?: string } }> | undefined,
): string {
  const iso = payload?.[0]?.payload?.iso;
  return iso ? fullDateFormatter.format(new Date(iso)) : "";
}
