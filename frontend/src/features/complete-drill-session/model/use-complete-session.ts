"use client";

import { trainerSessionsApi, type TrainerSessionSummary } from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Финализация DRILL/MOCK-сессии (#568): средний балл + статус COMPLETED.
 * После завершения грубо инвалидируем все trainer-кеши по префиксу ключа
 * `trainer-` (история сессий, статистика, mastery тем/прогресс) — пройденная
 * сессия меняет и историю, и mastery. Действие редкое, лишние рефетчи дёшевы
 * (зеркало coarse-helper'а `invalidateEducationContent` в ECS).
 */
export function useCompleteSession() {
  const queryClient = useQueryClient();
  return useMutation<TrainerSessionSummary, unknown, string>({
    mutationFn: (sessionId) => trainerSessionsApi.completeSession(sessionId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        predicate: (query) =>
          typeof query.queryKey[0] === "string" &&
          (query.queryKey[0] as string).startsWith("trainer-"),
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось завершить тренировку")),
  });
}
