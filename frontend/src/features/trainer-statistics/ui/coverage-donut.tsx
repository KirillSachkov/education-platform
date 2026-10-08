"use client";

import { Cell, Pie, PieChart, ResponsiveContainer } from "recharts";

import type { TrainerStudyStatusCount } from "@/entities/trainer-stats";
import { TRAINER_STUDY_STATUS_VISUALS } from "@/shared/config/trainer";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

/** Цвет сегмента статуса — в тон пилюлям `TRAINER_STUDY_STATUS_VISUALS`. */
const STATUS_COLOR: Record<string, string> = {
  KNOWN: "var(--green)",
  SEEN: "var(--muted-foreground)",
  REVIEW: "oklch(0.769 0.188 70.08)", // amber-500
  WRONG: "var(--destructive)",
};

/** Порядок сегментов: освоенные → проблемные. */
const STATUS_ORDER = ["KNOWN", "SEEN", "REVIEW", "WRONG"] as const;

/**
 * Блок 5 — покрытие материала (#568): донат распределения изученных вопросов по
 * статусу (Знаю / Видел / На повтор / Ошибка). Центр — всего изучено. Цвета — в
 * тон сквозным пилюлям статусов.
 */
export function CoverageDonut({
  breakdown,
  studiedQuestions,
}: {
  breakdown: TrainerStudyStatusCount[];
  studiedQuestions: number;
}) {
  const byStatus = new Map(breakdown.map((b) => [b.status, b.count]));
  const data = STATUS_ORDER.map((status) => ({
    status,
    label: TRAINER_STUDY_STATUS_VISUALS[status]?.label ?? status,
    value: byStatus.get(status) ?? 0,
    color: STATUS_COLOR[status],
  })).filter((d) => d.value > 0);

  return (
    <StatSection
      title="Изученные вопросы"
      icon={<TrainerStatIcon concept="coverage" className="size-4" />}
      hint="По текущему статусу — из вопросов, которые ты уже проходил (не процент от всех)."
      index={4}
    >
      {data.length === 0 ? (
        <p className="py-8 text-center text-sm text-muted-foreground">
          Изучай вопросы карточками — здесь появится покрытие.
        </p>
      ) : (
        <div className="flex flex-col items-center gap-4 sm:flex-row sm:gap-6">
          <div className="relative h-40 w-40 shrink-0">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={data}
                  dataKey="value"
                  nameKey="label"
                  innerRadius={52}
                  outerRadius={72}
                  paddingAngle={2}
                  strokeWidth={0}
                >
                  {data.map((d) => (
                    <Cell key={d.status} fill={d.color} />
                  ))}
                </Pie>
              </PieChart>
            </ResponsiveContainer>
            <div className="pointer-events-none absolute inset-0 grid place-items-center">
              <div className="text-center">
                <p className="text-2xl font-bold tabular-nums">{studiedQuestions}</p>
                <p className="text-[11px] text-muted-foreground">изучено</p>
              </div>
            </div>
          </div>

          <ul className="grid w-full grid-cols-2 gap-x-4 gap-y-2 sm:flex-1">
            {data.map((d) => (
              <li key={d.status} className="flex items-center gap-2 text-sm">
                <span
                  className="size-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: d.color }}
                  aria-hidden
                />
                <span className="text-muted-foreground">{d.label}</span>
                <span className="ml-auto font-semibold tabular-nums">{d.value}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </StatSection>
  );
}
