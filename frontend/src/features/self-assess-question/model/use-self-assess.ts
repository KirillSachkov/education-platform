"use client";

import { trainerQuestionsQueryOptions } from "@/entities/trainer-question";
import {
  trainerSessionQueryOptions,
  trainerSessionsApi,
  type SelfAssessmentDto,
} from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface SelfAssessVars {
  sessionId: string;
  itemId: string;
}

/**
 * Мягкая самооценка «Не уверен» (#691 t8): вопрос своей сессии паркуется в REVIEW (на повтор) —
 * без пометки «неверно» и без движения mastery. Идемпотентно. После успеха инвалидируем кеш
 * сессии (resume/review подтянет статус) + «Мои ошибки» + SRS-очередь «на повтор», где вопрос
 * теперь всплывёт. Ошибку показываем тостом.
 */
export function useSelfAssessQuestion() {
  const queryClient = useQueryClient();
  return useMutation<SelfAssessmentDto, unknown, SelfAssessVars>({
    mutationFn: ({ sessionId, itemId }) =>
      trainerSessionsApi.selfAssess(sessionId, itemId, { verdict: "UNSURE" }),
    onSuccess: async (_data, { sessionId }) => {
      toast.success("Отложено на повтор");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: trainerSessionQueryOptions.sessionKey(sessionId),
        }),
        queryClient.invalidateQueries({
          queryKey: [trainerQuestionsQueryOptions.mistakesBaseKey],
        }),
        queryClient.invalidateQueries({
          queryKey: [trainerQuestionsQueryOptions.srsBaseKey],
        }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось отложить вопрос")),
  });
}
