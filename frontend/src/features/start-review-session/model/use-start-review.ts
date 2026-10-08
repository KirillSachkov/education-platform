"use client";

import {
  trainerSessionsApi,
  type StartReviewSessionRequest,
  type TrainerSession,
} from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Старт REVIEW-сессии из произвольного набора вопросов (#568): тест (LEARN-движок,
 * мгновенная проверка + AI-грейд открытых) по конкретным `questionIds` — «Доучить»
 * (тест по N ошибкам), «Пройти тест по закладке», тест по вопросу из списка/SRS.
 * Возвращает сессию — вызывающий редиректит на `/trainer/session/{id}`.
 * Нет доступных вопросов → `trainer.review.no.questions`.
 */
export function useStartReview() {
  return useMutation<TrainerSession, unknown, StartReviewSessionRequest>({
    mutationFn: (request) => trainerSessionsApi.startReviewSession(request),
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось запустить тест")),
  });
}
