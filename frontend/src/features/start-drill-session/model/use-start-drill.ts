"use client";

import {
  trainerSessionsApi,
  type StartSessionRequest,
  type TrainerSession,
} from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Старт DRILL-сессии по теме (#568). Возвращает сессию со снапшотом вопросов
 * (без ключа грейдинга) — вызывающая страница редиректит на `/trainer/session/{id}`.
 * Locked-тема → 403 `trainer.topic.locked` (бэкенд отдаёт русский текст).
 */
export function useStartDrill() {
  return useMutation<TrainerSession, unknown, StartSessionRequest>({
    mutationFn: (request) => trainerSessionsApi.startSession({ mode: "DRILL", ...request }),
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось начать тренировку")),
  });
}
