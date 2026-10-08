"use client";

import { useQuery } from "@tanstack/react-query";
import { userProgressQueryOptions } from "../api";

export function useMyXpProgress() {
  const query = useQuery(userProgressQueryOptions.getMyXpProgressOptions());

  const userProgress = query.data;
  const totalXp = userProgress?.totalXp ?? 0;
  const currentLevel = userProgress?.currentLevel ?? 1;
  const nextLevel = userProgress?.nextLevel ?? null;
  const xpToNextLevel = userProgress?.xpToNextLevel ?? null;

  const nextLevelXpThreshold =
    xpToNextLevel === null ? totalXp : totalXp + xpToNextLevel;

  const progressPercent =
    xpToNextLevel === null
      ? 100
      : nextLevelXpThreshold === 0
        ? 0
        : (totalXp / nextLevelXpThreshold) * 100;

  return {
    totalXp,
    currentLevel,
    nextLevel,
    xpToNextLevel,
    nextLevelXpThreshold,
    progressPercent,
    isLoading: query.isLoading,
    error: query.error ?? null,
    refetch: query.refetch,
  };
}
