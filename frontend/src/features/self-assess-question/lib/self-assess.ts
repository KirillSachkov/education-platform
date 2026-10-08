/**
 * Показывать ли кнопку «Не уверен» (#691 t8) для текущего вопроса. Зеркалит гейт «Проверить»:
 * мягкая самооценка доступна только на НЕотвеченном вопросе в интерактивном PER_QUESTION-режиме —
 * не review (read-only разбор), не истёкший таймер MOCK (hard-stop), не grade-at-end тест (там
 * ответ принимается «вслепую», нечего откладывать) и не залоченный PRO-вопрос. В тесте
 * (END_OF_SESSION) и в review кнопки нет.
 */
export function canSelfAssess(params: {
  isReview: boolean;
  isHardStopped: boolean;
  isGradeAtEnd: boolean;
  isItemChecked: boolean;
  isItemSubmitted: boolean;
  isLocked: boolean;
}): boolean {
  return (
    !params.isReview &&
    !params.isHardStopped &&
    !params.isGradeAtEnd &&
    !params.isItemChecked &&
    !params.isItemSubmitted &&
    !params.isLocked
  );
}
