"use client";

import { useQuery } from "@tanstack/react-query";

import { TrainerStrengthsPanels, trainerStrengthsQueryOptions } from "@/entities/trainer-stats";
import { Skeleton } from "@/shared/ui/kit/skeleton";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

/**
 * Блок «Сильные и слабые темы» (#568, унифицирован #614 H2): стоит на ЕДИНОМ
 * измеренном per-topic mastery (`/trainer/stats/strengths`) — тот же источник, что и
 * на вкладке «Симуляция», поэтому списки не дрейфят. Рендерит общий
 * `TrainerStrengthsPanels` (две панели + строка объёма выборки + пустое состояние +
 * вторичная подсказка из моков). Клик по чипу → ИЗУЧЕНИЕ темы.
 */
export function StrengthsWeaknesses({ isAuthenticated }: { isAuthenticated: boolean }) {
  const strengthsQuery = useQuery({
    ...trainerStrengthsQueryOptions(),
    enabled: isAuthenticated,
  });

  return (
    <StatSection
      title="Сильные и слабые темы"
      icon={<TrainerStatIcon concept="mastery" className="size-4" />}
      index={3}
    >
      {strengthsQuery.isPending ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <Skeleton className="h-28 w-full rounded-xl" />
          <Skeleton className="h-28 w-full rounded-xl" />
        </div>
      ) : (
        <TrainerStrengthsPanels
          strong={strengthsQuery.data?.strongTopics ?? []}
          weak={strengthsQuery.data?.weakTopics ?? []}
          sample={strengthsQuery.data?.sampleSize}
          mockHints={strengthsQuery.data?.mockHintTopics ?? []}
        />
      )}
    </StatSection>
  );
}
