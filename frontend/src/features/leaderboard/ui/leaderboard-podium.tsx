"use client";

import type { LeaderboardUserDto } from "@/entities/leaderboard";
import { cn } from "@/shared/lib/css";
import { UserAvatar } from "@/shared/ui/components";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Crown, Medal, Trophy } from "lucide-react";

type Props = {
  users: LeaderboardUserDto[];
  isLoading: boolean;
  error: Error | null;
  onRetry: () => void;
};

const PODIUM_STYLES = [
  {
    container:
      "border-gold/40 bg-gradient-to-br from-gold/20 via-card to-gold/10",
    badge: "bg-gold text-black",
    icon: Crown,
    iconClass: "text-gold",
    label: "1 место",
  },
  {
    container:
      "border-slate-300/60 bg-gradient-to-br from-slate-300/20 via-card to-slate-100/10",
    badge: "bg-slate-400 text-slate-950",
    icon: Medal,
    iconClass: "text-slate-400",
    label: "2 место",
  },
  {
    container:
      "border-orange-400/35 bg-gradient-to-br from-orange-500/18 via-card to-orange-200/8",
    badge: "bg-orange-500 text-white",
    icon: Trophy,
    iconClass: "text-orange-500",
    label: "3 место",
  },
] as const;

function PodiumSkeleton() {
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      {[0, 1, 2].map((index) => (
        <Card key={index} className="border-border/60">
          <CardContent className="space-y-4 py-6">
            <div className="h-6 w-20 animate-pulse rounded-full bg-muted" />
            <div className="h-7 w-36 animate-pulse rounded bg-muted" />
            <div className="h-5 w-24 animate-pulse rounded bg-muted" />
            <div className="h-10 w-full animate-pulse rounded-2xl bg-muted" />
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function PodiumEmptyState() {
  return (
    <Card>
      <CardContent className="py-10 text-center">
        <h3 className="text-base font-semibold">Рейтинг пока пустой</h3>
        <p className="mt-2 text-sm text-muted-foreground">
          Как только пользователи начнут получать XP, здесь появятся лидеры.
        </p>
      </CardContent>
    </Card>
  );
}

export function LeaderboardPodium({
  users,
  isLoading,
  error,
  onRetry,
}: Props) {
  if (isLoading) {
    return <PodiumSkeleton />;
  }

  if (error) {
    return <ErrorCard error={error} onRetry={onRetry} />;
  }

  if (users.length === 0) {
    return <PodiumEmptyState />;
  }

  return (
    <div className="grid gap-4 lg:grid-cols-3">
      {users.map((user, index) => {
        const style = PODIUM_STYLES[index];
        const Icon = style.icon;
        const isChampion = index === 0;

        return (
          <div key={user.userId} className="relative">
            {/* Gold aura halo — only behind the champion, softly floats */}
            {isChampion && (
              <div
                className="absolute -inset-3 rounded-3xl bg-gold/20 blur-2xl opacity-60 animate-soft-float pointer-events-none"
                aria-hidden="true"
              />
            )}

            <Card
              className={cn(
                "relative overflow-hidden border shadow-sm transition-transform hover:-translate-y-0.5",
                style.container,
                user.isCurrentUser && "ring-2 ring-primary/40",
                isChampion && "shadow-xl shadow-gold/15",
              )}
            >
              {/* Diagonal shimmer sweep across the champion card */}
              {isChampion && (
                <div
                  className="absolute inset-0 pointer-events-none overflow-hidden"
                  aria-hidden="true"
                >
                  <div
                    className="absolute inset-y-0 -left-1/2 w-1/3 animate-shimmer-sweep"
                    style={{
                      background:
                        "linear-gradient(100deg, transparent, rgba(251, 224, 133, 0.18), transparent)",
                    }}
                  />
                </div>
              )}

              <CardContent className="relative space-y-5 py-6">
                <div className="flex items-center justify-between gap-3">
                  <span
                    className={cn(
                      "inline-flex rounded-full px-2.5 py-1 text-xs font-semibold",
                      style.badge,
                    )}
                  >
                    {style.label}
                  </span>

                  <Icon
                    className={cn(
                      "size-5",
                      style.iconClass,
                      isChampion && "size-6 drop-shadow-[0_0_8px_rgba(251,224,133,0.6)]",
                    )}
                  />
                </div>

                <div className="flex items-center gap-3">
                  <UserAvatar
                    name={user.displayName || user.username || "Пользователь"}
                    avatarId={user.avatarId}
                    className="size-14 shrink-0"
                  />
                  <div className="space-y-1">
                    <div className="text-xl font-bold">
                      {user.displayName || user.username || "Пользователь"}
                    </div>
                    <div className="text-sm text-muted-foreground">
                      Уровень {user.currentLevel}
                    </div>
                  </div>
                </div>

                <div className="rounded-2xl border border-border/50 bg-background/70 px-4 py-3">
                  <div className="text-2xl font-bold">
                    {user.totalXp.toLocaleString()} XP
                  </div>
                  <div className="mt-1 text-xs text-muted-foreground">
                    Ранг #{user.rank}
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>
        );
      })}
    </div>
  );
}
