import { isAxiosError } from "axios";

import { MAX_VOICE_ANSWER_LABEL } from "@/shared/config/trainer";

/**
 * Явная копия для случая «голосовой файл слишком большой» (#663). nginx режет запрос по
 * `client_max_body_size` РАНЬШЕ бэкенда → прилетает сырой HTTP 413 без envelope-кода, и общий
 * `getErrorMessage` показал бы невнятное «Request failed with status code 413» (что и видели ученики).
 * Возвращаем явное русское сообщение для этого случая; для остальных ошибок — `null` (их разберёт
 * `notifyTrainerError`: бэкендные `trainer.transcribe.too_long` / `invalid_audio` уже несут понятный
 * текст, а `trainer.pro.required` / `trainer.quota.exceeded` — с CTA на подписку).
 */
export function resolveVoiceTooLargeMessage(error: unknown): string | null {
  if (isAxiosError(error) && error.response?.status === 413) {
    return `Запись слишком большая. Запишите ответ короче — до ${MAX_VOICE_ANSWER_LABEL} — и попробуйте снова.`;
  }
  return null;
}
