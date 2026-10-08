import { toast } from "sonner";

import { getErrorCode, getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";

/**
 * Коды ошибок тренажёра, на которые имеет смысл подтолкнуть к подписке
 * Trainer Pro (#614 B2): полностью PRO-гейтнутое действие или исчерпанная квота
 * AI-проверок. На них тост получает доп-кнопку «Оформить подписку» → /trainer/pro.
 */
const PRO_NUDGE_CODES = new Set(["trainer.pro.required", "trainer.quota.exceeded"]);

/**
 * Показать тост ошибки тренажёрного действия. Сообщение берётся из i18n-слоя по
 * коду (`getErrorMessage`), а для PRO/quota-кодов добавляется CTA «Оформить
 * подписку» на страницу Trainer Pro — чтобы даже при обойдённом UI-гейте 403 был
 * полезным, а не сырым. Навигация — `window.location`, чтобы хелпер оставался
 * вне React-дерева (его зовут из `onError` мутаций).
 */
export function notifyTrainerError(error: unknown, fallback: string): void {
  const message = getErrorMessage(error, fallback);
  const code = getErrorCode(error);

  if (code && PRO_NUDGE_CODES.has(code)) {
    toast.error(message, {
      action: {
        label: "Оформить подписку",
        onClick: () => {
          window.location.assign(routes.trainerPro);
        },
      },
    });
    return;
  }

  toast.error(message);
}
