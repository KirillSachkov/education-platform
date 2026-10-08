"use client";

import {
  isAnswerAlreadyChecked,
  trainerSessionsApi,
  type CheckAnswerRequest,
  type CheckAnswerResponse,
} from "@/entities/trainer-session";
import { notifyTrainerError } from "@/shared/lib/trainer-error-toast";
import { useMutation } from "@tanstack/react-query";

interface CheckAnswerVars {
  sessionId: string;
  itemId: string;
  request: CheckAnswerRequest;
}

/**
 * Мгновенная проверка одного ответа в DRILL-сессии (#568). Сервер раскрывает
 * правильный ответ/эталон/разбор ИМЕННО этого вопроса (ключ не уезжает заранее).
 * Повторная проверка → 409 (вопрос уже зафиксирован). Кеш сессии не трогаем —
 * раннер держит результаты в локальном стейте, к завершению зовём `getSession`.
 * Исчерпанная квота AI-проверок (`trainer.quota.exceeded`) → тост с CTA на
 * подписку (#614 B2). 409 «ответ уже проверен» — НЕ показываем тост: раннер
 * обрабатывает его как success-path (#691 t4), раскрывая ответ из снапшота.
 */
export function useCheckAnswer() {
  return useMutation<CheckAnswerResponse, unknown, CheckAnswerVars>({
    mutationFn: ({ sessionId, itemId, request }) =>
      trainerSessionsApi.checkAnswer(sessionId, itemId, request),
    onError: (error) => {
      if (isAnswerAlreadyChecked(error)) return;
      notifyTrainerError(error, "Не удалось проверить ответ");
    },
  });
}
