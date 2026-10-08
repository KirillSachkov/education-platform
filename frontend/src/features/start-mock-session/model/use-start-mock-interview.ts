"use client";

import {
  trainerSessionsApi,
  type StartMockInterviewRequest,
  type TrainerSession,
} from "@/entities/trainer-session";
import { notifyTrainerError } from "@/shared/lib/trainer-error-toast";
import { useMutation } from "@tanstack/react-query";

/** Аргументы старта: id мок-собеседования + параметры сессии. */
export interface StartMockInterviewArgs extends StartMockInterviewRequest {
  interviewId: string;
}

/**
 * Старт MOCK-сессии из кураторского мок-собеседования (POSITION-подборка, #568).
 * Возвращает сессию со снапшотом вопросов (без ключа грейдинга) — вызывающая
 * страница редиректит на `/trainer/session/{id}`. Нет доступных вопросов →
 * `trainer.mock.no.questions` (бэкенд отдаёт русский текст). Симуляции —
 * PRO-only: `trainer.pro.required` / `trainer.quota.exceeded` маппятся в
 * дружелюбную копию с CTA на подписку (#614 B2).
 */
export function useStartMockInterview() {
  return useMutation<TrainerSession, unknown, StartMockInterviewArgs>({
    mutationFn: ({ interviewId, ...request }) =>
      trainerSessionsApi.startMockInterviewSession(interviewId, request),
    onError: (error) => notifyTrainerError(error, "Не удалось собрать симуляцию собеседования"),
  });
}
