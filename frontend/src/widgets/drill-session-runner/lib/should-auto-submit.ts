/**
 * Нужно ли авто-сохранить выбранный, но ещё не зафиксированный ответ ПЕРЕД уходом
 * с вопроса (#568). Действует во ВСЕХ режимах сессии (тест / тренировка / разбор
 * ошибок / симуляция): «Далее»/«Назад»/карта/«Завершить» сами сохраняют выбранный
 * ответ — отдельную кнопку «сохранить/ответить» жать не нужно. В PER_QUESTION
 * (тренировка/вопросы) кнопка «Проверить» остаётся как опциональный мгновенный разбор,
 * но НЕ требуется для записи ответа.
 *
 * Условия:
 *  - НЕ `isReview` — завершённую сессию листаем read-only.
 *  - НЕ `isHardStopped` — после истечения таймера симуляции ввод заблокирован.
 *  - НЕ `isItemRecorded` — ответ ещё не зафиксирован (проверен ИЛИ принят); иначе повтор → 409.
 *  - НЕ `isAnswerPending` — отправка уже идёт, второй вызов не нужен.
 *  - `hasAnswerDraft` — есть валидный черновик ответа (иначе нечего сохранять — это пропуск).
 */
export function shouldAutoSubmitOnLeave(args: {
  isReview: boolean;
  isHardStopped: boolean;
  isItemRecorded: boolean;
  isAnswerPending: boolean;
  hasAnswerDraft: boolean;
}): boolean {
  return (
    !args.isReview &&
    !args.isHardStopped &&
    !args.isItemRecorded &&
    !args.isAnswerPending &&
    args.hasAnswerDraft
  );
}
