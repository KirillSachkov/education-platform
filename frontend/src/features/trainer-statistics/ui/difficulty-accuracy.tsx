"use client";

import type { TrainerDifficultyAccuracy } from "@/entities/trainer-stats";
import { getMasteryTone, TRAINER_DIFFICULTY_VISUALS } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

const ORDER = ["JUNIOR", "MIDDLE", "SENIOR"] as const;

/**
 * Блок 6 — точность по сложности (#568): бар на каждый уровень J/M/S, цвет бара
 * по порогам освоения (`getMasteryTone`), бейдж сложности — сквозной
 * `TRAINER_DIFFICULTY_VISUALS`. Где силён, где проседаешь.
 */
export function DifficultyAccuracy({ items }: { items: TrainerDifficultyAccuracy[] }) {
  const byLevel = new Map(items.map((i) => [i.difficulty, i]));
  const hasData = items.some((i) => i.answered > 0);

  return (
    <StatSection
      title="Точность по сложности"
      icon={<TrainerStatIcon concept="accuracy" className="size-4" />}
      hint="Доля верных ответов по уровню сложности вопроса."
      index={5}
    >
      {!hasData ? (
        <p className="py-8 text-center text-sm text-muted-foreground">
          Ответь на вопросы разных уровней — увидишь, где силён.
        </p>
      ) : (
        <ul className="space-y-3.5">
          {ORDER.map((level) => {
            const row = byLevel.get(level);
            const answered = row?.answered ?? 0;
            const accuracy = row?.accuracyPercent ?? 0;
            const visual = TRAINER_DIFFICULTY_VISUALS[level];
            return (
              <li key={level} className="space-y-1.5">
                <div className="flex items-center justify-between gap-2 text-sm">
                  <span
                    className={cn(
                      "rounded-full px-2 py-0.5 text-[11px] font-semibold",
                      visual?.badgeClass,
                    )}
                  >
                    {visual?.label ?? level}
                  </span>
                  <span className="text-muted-foreground tabular-nums">
                    {answered > 0 ? (
                      <>
                        <span className="font-semibold text-foreground">{accuracy}%</span> ·{" "}
                        {answered} отв.
                      </>
                    ) : (
                      "нет ответов"
                    )}
                  </span>
                </div>
                <div className="h-1.5 overflow-hidden rounded-full bg-border/40">
                  <div
                    className={cn(
                      "h-full rounded-full transition-[width] duration-700",
                      getMasteryTone(accuracy),
                    )}
                    style={{ width: `${answered > 0 ? Math.max(accuracy, 2) : 0}%` }}
                  />
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </StatSection>
  );
}
