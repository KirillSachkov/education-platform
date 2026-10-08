import type { LeaderboardUserDto } from "@/entities/leaderboard";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { AnimatedNumber } from "@/shared/ui/components/animated-number";
import { Trophy } from "lucide-react";

type Props = {
  user: LeaderboardUserDto;
};

export function CurrentUserRankCard({ user }: Props) {
  return (
    <Card className="border-primary/25 bg-primary/5">
      <CardContent className="flex flex-col gap-4 py-5 sm:flex-row sm:items-center sm:justify-between">
        <div className="space-y-1">
          <p className="text-sm font-semibold text-primary">Ваше место</p>
          <h2 className="text-lg font-bold">
            #<AnimatedNumber value={user.rank} /> ·{" "}
            {user.displayName || user.username || "Пользователь"}
          </h2>
          <p className="text-sm text-muted-foreground">
            Вы не попали в текущий видимый список, но остаетесь в общем рейтинге.
          </p>
        </div>

        <div className="flex items-center gap-3 rounded-2xl border border-primary/15 bg-background px-4 py-3">
          <Trophy className="size-5 text-primary" />
          <div>
            <div className="text-sm text-muted-foreground">
              Уровень {user.currentLevel}
            </div>
            <div className="text-lg font-bold">
              <AnimatedNumber value={user.totalXp} /> XP
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
