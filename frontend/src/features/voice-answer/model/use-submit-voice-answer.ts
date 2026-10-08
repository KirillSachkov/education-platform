"use client";

import {
  isAnswerAlreadyChecked,
  trainerSessionsApi,
  type CheckAnswerResponse,
} from "@/entities/trainer-session";
import { notifyTrainerError } from "@/shared/lib/trainer-error-toast";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

import { resolveVoiceTooLargeMessage } from "../lib/voice-upload-error";

interface SubmitVoiceAnswerVars {
  sessionId: string;
  itemId: string;
  audio: Blob;
}

/**
 * Отправка голосового ответа на открытый вопрос (#585): шлёт записанный Blob на
 * бэкенд, который сам транскрибирует и грейдит — клиент текст НЕ распознаёт.
 * Возвращает `CheckAnswerResponse` (для мока END_OF_SESSION вердикт PENDING,
 * ключ скрыт — разбор после Complete). Ошибку показываем тостом; аудио НЕ
 * теряем — вызывающий оставляет запись, чтобы можно было отправить повторно.
 * PRO-гейт голоса (`trainer.pro.required`) / квота / слишком длинная запись
 * (`trainer.transcribe.too_long`) маппятся в дружелюбную копию, для PRO/quota —
 * с CTA на подписку (#614 B2). Сырой HTTP 413 от nginx (файл больше лимита
 * `client_max_body_size`, без envelope-кода) → явное «запись слишком большая»
 * вместо «Request failed with status code 413» (#663). 409 «ответ уже проверен»
 * тоста НЕ даёт — раннер обрабатывает его как success-path (#691 t4).
 */
export function useSubmitVoiceAnswer() {
  return useMutation<CheckAnswerResponse, unknown, SubmitVoiceAnswerVars>({
    mutationFn: ({ sessionId, itemId, audio }) =>
      trainerSessionsApi.submitVoiceAnswer(sessionId, itemId, audio),
    onError: (error) => {
      if (isAnswerAlreadyChecked(error)) return;
      const tooLarge = resolveVoiceTooLargeMessage(error);
      if (tooLarge) {
        toast.error(tooLarge);
        return;
      }
      notifyTrainerError(error, "Не удалось отправить запись");
    },
  });
}
