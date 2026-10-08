"use client";

import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useMyXpProgress } from "../model/use-my-xp-progress";

type MyXpProgressCardProps = {
  className?: string;
};

function MyXpProgressCardSkeleton({ className }: MyXpProgressCardProps) {
  return (
    <Card className={cn("p-4 gap-3", className)}>
      <div className="flex items-center gap-3">
        <div className="size-8 rounded-full bg-muted animate-pulse" />
        <div className="space-y-2">
          <div className="h-4 w-24 rounded bg-muted animate-pulse" />
          <div className="h-3 w-20 rounded bg-muted animate-pulse" />
        </div>
        <div className="ml-auto h-4 w-16 rounded bg-muted animate-pulse" />
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <div className="h-3 w-24 rounded bg-muted animate-pulse" />
          <div className="h-3 w-28 rounded bg-muted animate-pulse" />
        </div>
        <div className="h-2 w-full rounded bg-muted animate-pulse" />
      </div>
    </Card>
  );
}

export function MyXpProgressCard({ className }: MyXpProgressCardProps) {
  const {
    totalXp,
    currentLevel,
    nextLevel,
    xpToNextLevel,
    nextLevelXpThreshold,
    progressPercent,
    isLoading,
    error,
    refetch,
  } = useMyXpProgress();

  if (isLoading) {
    return <MyXpProgressCardSkeleton className={className} />;
  }

  if (error) {
    return (
      <Card className={className}>
        <CardContent>
          <ErrorCard error={error} onRetry={() => void refetch()} />
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className={cn("p-4 gap-3 border-gold/20", className)}>
      <div className="flex items-center gap-3">
        <div className="size-8 rounded-full bg-gold/15 flex items-center justify-center text-gold font-bold text-xs">
          {currentLevel}
        </div>
        <div>
          <div className="text-sm font-medium">Уровень {currentLevel}</div>
        </div>
        <div className="ml-auto flex items-center gap-1.5">
          <Icons.trophy size={14} className="text-gold" />
          <span className="text-xs font-semibold text-gold">
            {totalXp.toLocaleString()} XP
          </span>
        </div>
      </div>

      <div className="space-y-1.5">
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span>Прогресс уровня</span>
          <span>
            {totalXp.toLocaleString()} / {nextLevelXpThreshold.toLocaleString()}{" "}
            XP
          </span>
        </div>

        <div className="relative h-2 w-full rounded-full bg-secondary border border-border/40 overflow-hidden">
          <div
            className="absolute inset-y-0 left-0 rounded-full bg-gradient-to-r from-gold/80 via-gold to-gold/90 shadow-[0_0_12px] shadow-gold/40 transition-[width] duration-700 ease-out"
            style={{ width: `${progressPercent}%` }}
          />
          {progressPercent > 0 && (
            <div
              className="absolute inset-y-0 left-0 overflow-hidden rounded-full pointer-events-none"
              style={{ width: `${progressPercent}%` }}
            >
              <div
                className="absolute inset-y-0 -left-1/2 w-1/2 animate-shimmer-sweep"
                style={{
                  background:
                    "linear-gradient(90deg, transparent, rgba(255,255,255,0.4), transparent)",
                }}
              />
            </div>
          )}
        </div>

        <p className="text-xs text-muted-foreground">
          {nextLevel === null || xpToNextLevel === null
            ? "Максимальный уровень достигнут"
            : `Ещё ${xpToNextLevel.toLocaleString()} XP до уровня ${nextLevel}`}
        </p>
      </div>
    </Card>
  );
}
