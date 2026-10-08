"use client";

import {
  trainerSessionQueryOptions,
  trainerSessionsApi,
  type AiFeedbackRatingDto,
  type TrainerFeedbackRating,
} from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface RateAiFeedbackVars {
  sessionId: string;
  itemId: string;
  rating: TrainerFeedbackRating;
}

/**
 * Оценка студентом AI-разбора («Разбор ИИ») открытого ответа 👍/👎 (#691 t7). Апсерт идемпотентен
 * (та же оценка = no-op, противоположная = переключение). Оптимистичный тоггл держит вызывающий
 * компонент в локальном стейте (как `bookmarked` в раннере) и откатывает на ошибке; после успеха
 * инвалидируем кеш сессии, чтобы review/resume подтянул свежее состояние. Ошибку показываем тостом.
 */
export function useRateAiFeedback() {
  const queryClient = useQueryClient();
  return useMutation<AiFeedbackRatingDto, unknown, RateAiFeedbackVars>({
    mutationFn: ({ sessionId, itemId, rating }) =>
      trainerSessionsApi.rateAiFeedback(sessionId, itemId, { rating }),
    onSuccess: async (_data, { sessionId }) => {
      await queryClient.invalidateQueries({
        queryKey: trainerSessionQueryOptions.sessionKey(sessionId),
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось отправить оценку")),
  });
}
