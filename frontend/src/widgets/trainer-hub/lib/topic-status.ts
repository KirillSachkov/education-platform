export type TrainerTopicStudyStatusKind =
  | "locked"
  | "not-started"
  | "needs-work"
  | "review-errors"
  | "in-progress"
  | "strong";

export interface TrainerTopicStudyStatusInput {
  masteryPercent: number;
  /**
   * «Освоение» = покрытие темы 0..100 (#664). Решает статус «Хорошо изучена» (только при высоком
   * покрытии), а НЕ EWMA-`masteryPercent`. Не передан → 0 (как «ничего не освоено»).
   */
  coveragePercent?: number;
  isWeak: boolean;
  answersCount: number;
  studiedCount?: number;
  mistakesCount?: number;
  isLocked?: boolean;
}

export interface TrainerTopicStudyStatus {
  kind: TrainerTopicStudyStatusKind;
  label: string;
  hasActivity: boolean;
}

export function getTrainerTopicStudyStatus({
  masteryPercent,
  coveragePercent = 0,
  isWeak,
  answersCount,
  studiedCount = 0,
  mistakesCount = 0,
  isLocked = false,
}: TrainerTopicStudyStatusInput): TrainerTopicStudyStatus {
  const hasActivity = answersCount > 0 || studiedCount > 0;

  if (!hasActivity) {
    return isLocked
      ? { kind: "locked", label: "По плану", hasActivity }
      : { kind: "not-started", label: "Не изучена", hasActivity };
  }

  const highMistakePressure =
    answersCount > 0 && mistakesCount >= Math.max(2, Math.ceil(answersCount * 0.3));

  if (answersCount > 0 && (isWeak || masteryPercent < 60 || highMistakePressure)) {
    return { kind: "needs-work", label: "Нужно подтянуть", hasActivity };
  }

  if (mistakesCount > 0) {
    return { kind: "review-errors", label: "Повторить ошибки", hasActivity };
  }

  // «Хорошо изучена» = высокое ПОКРЫТИЕ темы (#664), а не EWMA-mastery: 1-2 верных ответа из
  // десятка вопросов дают ~8-20% покрытия → останутся «В процессе».
  if (coveragePercent >= 80) {
    return { kind: "strong", label: "Хорошо изучена", hasActivity };
  }

  return { kind: "in-progress", label: "В процессе", hasActivity };
}
