"use client";

import { trainerSessionQueryOptions } from "@/entities/trainer-session";
import { useMyXpProgress } from "@/entities/user-progress";
import { cn } from "@/shared/lib/css";
import { AnimatedNumber } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { computeActivityStreak, pluralizeDays } from "../lib/compute-streak";

/**
 * Лёгкий стрик/XP-индикатор хаба (#568 Ф2): «N дней подряд» (выведено из
 * таймстемпов сессий — без нового бэкенда) + платформенный уровень/XP
 * (`useMyXpProgress`, reuse #555). Сдержанные текстовые пилюли в духе
 * минимализма; рендерим только для залогиненного и только непустые сигналы.
 * Если XP/прогресс недоступен — тихо опускаем XP-пилюлю (стрик остаётся).
 */
export function StreakIndicator({ isAuthenticated }: { isAuthenticated: boolean }) {
  const historyQuery = useQuery({
    ...trainerSessionQueryOptions.historyOptions(),
    enabled: isAuthenticated,
  });
  const xp = useMyXpProgress();

  if (!isAuthenticated) return null;

  const history = historyQuery.data ?? [];
  const streak = computeActivityStreak(
    history.map((session) => session.completedAt ?? session.startedAt),
  );
  const showXp = !xp.isLoading && !xp.error && xp.totalXp > 0;

  if (streak === 0 && !showXp) return null;

  return (
    <div className="flex flex-wrap items-center gap-2">
      {streak > 0 && (
        <Pill tone="streak">
          <Icons.streak className="size-3.5" aria-hidden="true" />
          {streak} {pluralizeDays(streak)} подряд
        </Pill>
      )}
      {showXp && (
        <Pill tone="xp">
          <Icons.xp className="size-3.5" aria-hidden="true" />
          Уровень {xp.currentLevel}
          <span className="text-muted-foreground">
            · <AnimatedNumber value={xp.totalXp} /> XP
          </span>
        </Pill>
      )}
    </div>
  );
}

function Pill({ tone, children }: { tone: "streak" | "xp"; children: React.ReactNode }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-md px-2.5 py-1 text-xs font-medium",
        tone === "streak" && "bg-amber-500/10 text-amber-600 dark:text-amber-400",
        tone === "xp" && "bg-primary/10 text-primary",
      )}
    >
      {children}
    </span>
  );
}
