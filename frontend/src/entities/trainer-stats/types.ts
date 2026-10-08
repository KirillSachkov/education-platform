/**
 * DTO страницы статистики тренажёра (#568, `/trainer/stats`). Зеркалят контракты
 * `TrainerService.Contracts/Stats` (camelCase после сериализации). Все данные —
 * scope текущего юзера; кросс-юзерных агрегатов нет (сознательно, см. спек).
 */

/** Одна точка дневной активности (агрегат сессий за дату). */
export interface TrainerActivityDay {
  /** ISO-дата `YYYY-MM-DD` (UTC). */
  date: string;
  sessions: number;
  answered: number;
  correct: number;
  accuracyPercent: number;
}

/** Серия дневной активности + streak'и (для heatmap + графика по дням). */
export interface TrainerActivity {
  days: TrainerActivityDay[];
  /** Дней подряд с активностью, до сегодня. */
  currentStreak: number;
  longestStreak: number;
}

/** Счётчик статуса изучения вопроса (для доната покрытия). NEW не материализуется. */
export interface TrainerStudyStatusCount {
  status: "SEEN" | "KNOWN" | "REVIEW" | "WRONG";
  count: number;
}

/** Точность по уровню сложности (где силён/слаб). */
export interface TrainerDifficultyAccuracy {
  difficulty: "JUNIOR" | "MIDDLE" | "SENIOR";
  answered: number;
  correct: number;
  accuracyPercent: number;
}

/** Прогноз SRS на один будущий день. */
export interface TrainerSrsForecastDay {
  date: string;
  due: number;
}

export interface TrainerStatsSrs {
  /** Вопросов «на повтор сегодня» (`nextDueAt <= now`). */
  dueToday: number;
  /** Сколько подойдёт к повтору в следующие 7 дней. */
  upcoming: TrainerSrsForecastDay[];
  /** Удержание: timesKnown / timesSeen, %. */
  retentionPercent: number;
}

/** Сводка-агрегаты (всё, кроме дневной серии — она в `TrainerActivity`). */
export interface TrainerStatsSummary {
  totalAnswered: number;
  allTimeAccuracyPercent: number;
  studyStatusBreakdown: TrainerStudyStatusCount[];
  studiedQuestions: number;
  difficultyAccuracy: TrainerDifficultyAccuracy[];
  srs: TrainerStatsSrs;
}

/** Одна завершённая мок-сессия в тренде. */
export interface TrainerMockAttempt {
  sessionId: string;
  completedAt: string;
  scorePercent: number;
}

/** Тренд мок-собесов + агрегированные слабые/сильные темы из AI-фидбэка. */
export interface TrainerMockTrend {
  attempts: TrainerMockAttempt[];
  weakTopics: string[];
  strongTopics: string[];
}

/** Одна тема в рейтинге сильных/слабых сторон по измеренному mastery (#614 H). */
export interface TrainerStrengthTopic {
  topicId: string;
  title: string;
  /** Измеренный mastery 0..100 (EWMA по ответам). */
  masteryPercent: number;
  /** Сколько оценённых ответов питает оценку. */
  attempts: number;
}

/** Объём данных, на котором стоит оценка сильных/слабых сторон (контекст «sample size»). */
export interface TrainerStrengthSampleSize {
  /** Тем, прошедших порог попыток (попали в оценку). */
  assessedTopics: number;
  /** Всего оценённых ответов, питающих оценку. */
  totalAnswers: number;
  /** Сессий пройдено всего. */
  sessionsCount: number;
  /** Из них — мок-собесов. */
  mockCount: number;
}

/**
 * Сильные и слабые стороны по ИЗМЕРЕННОМУ mastery поверх всей активности тренажёра (#614 H).
 * `mockHintTopics` — де-шумленный вторичный сигнал из AI-разбора моков (тема — только если в ≥2 моках).
 */
export interface TrainerStrengths {
  strongTopics: TrainerStrengthTopic[];
  weakTopics: TrainerStrengthTopic[];
  sampleSize: TrainerStrengthSampleSize;
  mockHintTopics: string[];
}

/**
 * Динамика mastery по одной теме «vs месяц назад» (#681 T6). `masteryThen`/`delta` = null,
 * если у темы ещё нет исторического снимка (ранний пользователь). Положительная `delta` —
 * тема подтянулась за месяц.
 */
export interface TrainerTopicMasteryTrend {
  topicId: string;
  topicTitle: string;
  masteryNow: number;
  masteryThen: number | null;
  delta: number | null;
}

/**
 * Тренд mastery вызывающего «vs месяц назад» (#681 T6): per-topic снимок сегодня vs
 * `comparisonDays`-дневной давности + общая средняя дельта. Overall-поля считаются только по
 * «сравнимым» темам (есть оба снимка) → все три = null, если истории ни у одной темы нет.
 */
export interface TrainerTrends {
  /** На сколько дней назад берётся «было» (фиксировано 30). */
  comparisonDays: number;
  topics: TrainerTopicMasteryTrend[];
  overallMasteryNow: number | null;
  overallMasteryThen: number | null;
  overallDelta: number | null;
}
