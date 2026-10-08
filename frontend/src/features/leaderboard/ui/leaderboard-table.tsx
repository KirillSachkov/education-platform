"use client";

import type { LeaderboardUserDto } from "@/entities/leaderboard";
import { cn } from "@/shared/lib/css";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { UserAvatar } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/kit/table";
import { ChevronLeft, ChevronRight, Crown, Medal, Trophy } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";

type Props = {
  users: LeaderboardUserDto[];
  page: number;
  totalPages: number;
  isLoading: boolean;
  error: Error | null;
  onRetry: () => void;
  onPageChange: (page: number) => void;
};

const RANK_ICONS: Record<number, { icon: typeof Crown; className: string }> = {
  1: { icon: Crown, className: "text-gold" },
  2: { icon: Medal, className: "text-slate-400" },
  3: { icon: Trophy, className: "text-orange-500" },
};

// #572 — фон для топ-3 (gold / silver / bronze), читаемый в dark-теме.
// hover-варианты усилены, чтобы подсветка не «гасла» при наведении.
const RANK_ROW_BG: Record<number, string> = {
  1: "bg-gold/10 hover:bg-gold/20",
  2: "bg-slate-400/10 hover:bg-slate-400/20",
  3: "bg-orange-500/10 hover:bg-orange-500/20",
};

// #572 — глобальные счётчики активности. Порядок: Задачи → Тесты → Уроки.
const ACTIVITY_STATS: {
  key: "issuesCompleted" | "quizzesCompleted" | "materialsCompleted";
  icon: (typeof Icons)["issue"];
  label: string;
  title: string;
}[] = [
  { key: "issuesCompleted", icon: Icons.issue, label: "Задачи", title: "Решено задач" },
  { key: "quizzesCompleted", icon: Icons.quiz, label: "Тесты", title: "Пройдено тестов" },
  { key: "materialsCompleted", icon: Icons.lesson, label: "Уроки", title: "Изучено уроков" },
];

function TableSkeleton() {
  return (
    <Card>
      <CardContent className="py-6">
        <div className="space-y-3">
          {[0, 1, 2, 3, 4].map((index) => (
            <div
              key={index}
              className="h-12 animate-pulse rounded-xl bg-muted"
            />
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

function TableEmptyState() {
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

export function LeaderboardTable({
  users,
  page,
  totalPages,
  isLoading,
  error,
  onRetry,
  onPageChange,
}: Props) {
  const router = useRouter();

  if (isLoading) {
    return <TableSkeleton />;
  }

  if (error) {
    return <ErrorCard error={error} onRetry={onRetry} />;
  }

  if (users.length === 0) {
    return <TableEmptyState />;
  }

  return (
    <section className="space-y-4">
      <Card className="overflow-hidden">
        <CardContent className="px-0 py-0">
          <Table className="table-fixed">
            <TableHeader>
              <TableRow>
                <TableHead className="w-12 sm:w-16 pl-4 sm:pl-6">#</TableHead>
                <TableHead>Пользователь</TableHead>
                {ACTIVITY_STATS.map((stat) => (
                  <TableHead
                    key={stat.key}
                    className="hidden md:table-cell w-16 text-center"
                    title={stat.title}
                  >
                    {stat.label}
                  </TableHead>
                ))}
                <TableHead className="hidden lg:table-cell w-28">Уровень</TableHead>
                <TableHead className="w-24 sm:w-28 pr-4 sm:pr-6 text-right">XP</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((user) => {
                const rankInfo = RANK_ICONS[user.rank];
                const RankIcon = rankInfo?.icon;

                return (
                  <TableRow
                    key={user.userId}
                    className={cn(
                      "cursor-pointer transition-colors hover:bg-accent/40",
                      RANK_ROW_BG[user.rank],
                      user.isCurrentUser && "bg-primary/10 hover:bg-primary/15",
                    )}
                    onClick={() => router.push(routes.userProfile(user.userId))}
                  >
                    <TableCell className="pl-4 sm:pl-6">
                      <div className="flex items-center gap-1.5">
                        {RankIcon ? (
                          <RankIcon
                            className={cn("size-4", rankInfo.className)}
                          />
                        ) : (
                          <span className="text-muted-foreground">
                            {user.rank}
                          </span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2 min-w-0">
                        <UserAvatar
                          name={user.displayName || user.username}
                          avatarId={user.avatarId}
                          className="size-6 sm:size-7 shrink-0"
                        />
                        <div className="min-w-0">
                          <Link
                            href={routes.userProfile(user.userId)}
                            onClick={(e) => e.stopPropagation()}
                            className={cn(
                              "font-medium truncate text-sm hover:underline block",
                              rankInfo && "font-semibold",
                            )}
                          >
                            {user.displayName || user.username || "Пользователь"}
                          </Link>
                          {/* Mobile-only компактная строка статистики (на md+ — отдельные колонки). */}
                          <div className="flex md:hidden items-center gap-2.5 mt-0.5 text-2xs text-muted-foreground">
                            {ACTIVITY_STATS.map((stat) => {
                              const StatIcon = stat.icon;
                              return (
                                <span
                                  key={stat.key}
                                  className="inline-flex items-center gap-0.5"
                                  title={stat.title}
                                >
                                  <StatIcon className="size-3" />
                                  {user[stat.key]}
                                </span>
                              );
                            })}
                          </div>
                        </div>
                        {user.isCurrentUser && (
                          <span className="shrink-0 rounded-full bg-primary/10 px-1.5 py-0.5 text-2xs font-semibold text-primary">
                            Вы
                          </span>
                        )}
                      </div>
                    </TableCell>
                    {ACTIVITY_STATS.map((stat) => (
                      <TableCell
                        key={stat.key}
                        className="hidden md:table-cell text-center text-sm tabular-nums text-muted-foreground"
                      >
                        {user[stat.key]}
                      </TableCell>
                    ))}
                    <TableCell className="hidden lg:table-cell">Уровень {user.currentLevel}</TableCell>
                    <TableCell
                      className={cn(
                        "pr-4 sm:pr-6 text-right font-semibold text-sm tabular-nums",
                        rankInfo && "text-gold",
                      )}
                    >
                      {user.totalXp.toLocaleString()} XP
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(Math.max(1, page - 1))}
            disabled={page <= 1}
          >
            <ChevronLeft className="size-4" />
          </Button>

          <span className="text-sm text-muted-foreground">
            {page} / {totalPages}
          </span>

          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(Math.min(totalPages, page + 1))}
            disabled={page >= totalPages}
          >
            <ChevronRight className="size-4" />
          </Button>
        </div>
      )}
    </section>
  );
}
