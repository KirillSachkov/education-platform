"use client";

import { useLeaderboard } from "@/entities/leaderboard";
import { useState } from "react";
import { CurrentUserRankCard } from "./current-user-rank-card";
import { LeaderboardTable } from "./leaderboard-table";

const PAGE_SIZE = 20;

export function LeaderboardPage() {
  const [page, setPage] = useState(1);

  const leaderboardQuery = useLeaderboard({
    page,
    pageSize: PAGE_SIZE,
  });

  const leaderboard = leaderboardQuery.data;
  const currentUser = leaderboard?.currentUser ?? null;
  const users = leaderboard?.items ?? [];

  const visibleUserIds = new Set(users.map((user) => user.userId));
  const shouldShowCurrentUserCard =
    currentUser !== null && !visibleUserIds.has(currentUser.userId);

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 md:gap-8 p-4 md:p-6">
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
