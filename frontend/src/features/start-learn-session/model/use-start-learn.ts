"use client";

import {
  trainerSessionsApi,
  type StartLearnSessionRequest,
  type TrainerSession,
} from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Старт LEARN-сессии («обучение по тестам», #568 Ф2). Адаптивный мини-тест с
 * мгновенным фидбэком (PER_QUESTION); ошибочные повторяются. Возвращает сессию
 * со снапшотом вопросов — вызывающий редиректит на `/trainer/session/{id}`.
 * Нет вопросов → `trainer.learn.no.questions`. Locked-тема → 403.
 */
export function useStartLearn() {
  return useMutation<TrainerSession, unknown, StartLearnSessionRequest>({
    mutationFn: (request) => trainerSessionsApi.startLearnSession(request),
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось начать обучение")),
  });
}
