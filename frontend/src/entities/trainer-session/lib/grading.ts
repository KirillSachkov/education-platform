import type { TrainerGradingStatus } from "../types";

/**
 * Идёт ли AI-грейдинг мок-симуляции прямо сейчас (#585). PENDING/GRADING — фоновый
 * грейдер ещё работает (он серверный и переживает уход со страницы / рестарт). На
 * этот сигнал завязаны поллинг сессии/истории (раз в 3с) и индикатор «ИИ проверяет…».
 * Терминальные NOT_REQUIRED/GRADED/FAILED → проверка не идёт.
 */
export function isGradingInProgress(status: TrainerGradingStatus | null | undefined): boolean {
  return status === "PENDING" || status === "GRADING";
}
