"use client";

import { useState } from "react";
import { useLeaderboard } from "@/entities/leaderboard";
import { CurrentUserRankCard, LeaderboardTable } from "@/features/leaderboard";

const PAGE_SIZE = 20;

export function PlatformLeaderboardClient() {
  const [page, setPage] = useState(1);

  const leaderboardQuery = useLeaderboard({
    page,
    pageSize: PAGE_SIZE,
  });

  const leaderboard = leaderboardQuery.data;
  const currentUser = leaderboard?.currentUser ?? null;
  const users = leaderboard?.items ?? [];
  const totalCount = leaderboard?.totalCount ?? 0;

  const visibleUserIds = new Set(users.map((user) => user.userId));
  const shouldShowCurrentUserCard =
    currentUser !== null && !visibleUserIds.has(currentUser.userId);

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 md:gap-8 p-4 md:p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">
          Рейтинг платформы
        </h1>
        <p className="max-w-2xl text-sm text-muted-foreground sm:text-base">
          Общий зачёт по суммарному XP — все ученики со всех авторов и курсов в
          одной таблице.
        </p>
        {totalCount > 0 && (
          <p className="text-sm text-muted-foreground">
            В рейтинге сейчас{" "}
            <span className="font-semibold text-foreground">
              {totalCount.toLocaleString("ru-RU")}
            </span>{" "}
            участников
          </p>
        )}
      </header>

      {shouldShowCurrentUserCard && <CurrentUserRankCard user={currentUser} />}

      <LeaderboardTable
        users={users}
        page={page}
        totalPages={leaderboard?.totalPages ?? 0}
        isLoading={leaderboardQuery.isLoading}
        error={leaderboardQuery.error}
        onRetry={() => void leaderboardQuery.refetch()}
        onPageChange={setPage}
      />
    </div>
  );
}
