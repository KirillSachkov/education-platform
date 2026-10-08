// Mirrors backend `TrainerService.Contracts.Admin.*` DTOs (#614 D1 + #681/#680).
// JSON is camelCase. Cost fields are in micro-rubles (₽×1e6) unless suffixed `Rub`.

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats — composite money + usage snapshot (#614 D1, money #680)
// ─────────────────────────────────────────────────────────────────────────────

/** Стоимость + число вызовов по одному типу AI-операции. */
export interface AdminAiOperationBreakdown {
  operation: string;
  count: number;
  costMicroRub: number;
}

/** Стоимость + токены + число вызовов по одной AI-модели. */
export interface AdminAiModelBreakdown {
  model: string;
  count: number;
  costMicroRub: number;
  inputTokens: number;
  outputTokens: number;
}

/** Один календарный день ряда стоимости (плотный — нулевые дни заполнены). */
export interface AdminAiDailyPoint {
  /** ISO date (`YYYY-MM-DD`). */
  date: string;
  costMicroRub: number;
}

/**
 * Один пользователь из топа по тратам. `displayName`/`avatarUrl` — best-effort из AuthService
 * (#680); оба `null`, если не резолвится — фронт показывает короткий id. `avatarUrl` —
 * origin-relative путь FileService (`/api/files/{id}/content`), готовый к рендеру.
 */
export interface AdminAiTopUser {
  userId: string;
  costMicroRub: number;
  operationCount: number;
  displayName: string | null;
  avatarUrl: string | null;
}

/**
 * Производные «деньги»-метрики AI-расходов окна (#681/#680): стоимость на пользователя /
 * сессию / проверку открытого ответа + проекция расхода на 30 дней. Каждая — в микрорублях
 * + рублях. Нулевой знаменатель ⇒ метрика 0.
 */
export interface AdminAiMoneyMetrics {
  costPerUserMicroRub: number;
  costPerUserRub: number;
  costPerSessionMicroRub: number;
  costPerSessionRub: number;
  costPerGradeMicroRub: number;
  costPerGradeRub: number;
  projectedMonthMicroRub: number;
  projectedMonthRub: number;
}

/** AI-расходы тренажёра за окно. */
export interface AdminAiSpend {
  totalCostMicroRub: number;
  /** Та же сумма в рублях (= micro/1e6) — для показа. */
  totalCostRub: number;
  totalOperations: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  byOperation: AdminAiOperationBreakdown[];
  byModel: AdminAiModelBreakdown[];
  daily: AdminAiDailyPoint[];
  topUsers: AdminAiTopUser[];
  money: AdminAiMoneyMetrics;
}

/** Сколько сессий начато в окне по одному режиму (DRILL/LEARN/MOCK). */
export interface AdminUsageModeBreakdown {
  mode: string;
  count: number;
}

/** Использование тренажёра за окно. */
export interface AdminUsage {
  sessionsStarted: number;
  byMode: AdminUsageModeBreakdown[];
  activeUsers: number;
  completedSessions: number;
}

/** Композитный admin-снимок тренажёра за окно `days` (#614 D1). */
export interface AdminStats {
  days: number;
  aiSpend: AdminAiSpend;
  usage: AdminUsage;
}

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats/traffic — DAU/WAU/MAU, retention, new-vs-returning (#681 T3)
// ─────────────────────────────────────────────────────────────────────────────

/** Активные пользователи в скользящих окнах 1/7/30 дней (не зависят от `days`). */
export interface AdminActiveUsers {
  dau: number;
  wau: number;
  mau: number;
}

/** Одна retention-метрика: размер когорты, сколько вернулось, доля (0..1). */
export interface AdminRetentionBucket {
  cohortSize: number;
  returnedCount: number;
  rate: number;
}

/** Retention по first-touch когортам за окно (D1/D7/D30). */
export interface AdminRetention {
  d1: AdminRetentionBucket;
  d7: AdminRetentionBucket;
  d30: AdminRetentionBucket;
}

/** Один день окна: новые vs вернувшиеся активные пользователи. Плотный ряд. */
export interface AdminNewReturningDay {
  date: string;
  newUsers: number;
  returningUsers: number;
}

/** Один день окна: число начатых сессий по режиму. Плотный ряд. */
export interface AdminSessionsByModeDay {
  date: string;
  drill: number;
  learn: number;
  mock: number;
}

/** Admin-снимок трафика тренажёра за окно `days` (#681 T3). */
export interface AdminTrafficStats {
  days: number;
  activeUsers: AdminActiveUsers;
  retention: AdminRetention;
  newVsReturning: AdminNewReturningDay[];
  sessionsByMode: AdminSessionsByModeDay[];
}

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats/funnel — completion, drop-off, abandoned mocks (#681 T3)
// ─────────────────────────────────────────────────────────────────────────────

/** Старт→завершение по всем режимам: начато / завершено / доля (0..1). */
export interface AdminCompletion {
  started: number;
  completed: number;
  rate: number;
}

/** Старт→завершение по одному режиму. */
export interface AdminModeCompletion {
  mode: string;
  started: number;
  completed: number;
  rate: number;
}

/** Одна ординальная позиция вопроса: дошло / ответило / доля ответивших. */
export interface AdminDropOffPosition {
  position: number;
  reached: number;
  answered: number;
  answeredRate: number;
}

/** Брошенные мок-собесы: начато / не завершено / доля брошенных. */
export interface AdminAbandonedMocks {
  mockStarted: number;
  mockAbandoned: number;
  abandonRate: number;
}

/** Admin-снимок воронки тренажёра за окно `days` (#681 T3). */
export interface AdminFunnelStats {
  days: number;
  completion: AdminCompletion;
  completionByMode: AdminModeCompletion[];
  dropOffByPosition: AdminDropOffPosition[];
  abandonedMocks: AdminAbandonedMocks;
}

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats/question-quality — per-question quality (#681 T4)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * OPEN_TEXT-специфика: разбивка вердиктов AI-грейда + распределение балла по бэндам
 * (0–39 / 40–79 / 80–100, зеркалят грейдер #678).
 */
export interface AdminOpenTextQuality {
  correct: number;
  partial: number;
  incorrect: number;
  scoreBucketLow: number;
  scoreBucketMid: number;
  scoreBucketHigh: number;
}

/** Метрики качества одного вопроса. `*Rate` поля 0..1, null при нулевом знаменателе. */
export interface AdminQuestionQualityItem {
  questionId: string;
  stem: string;
  questionType: string;
  difficulty: string | null;
  section: string | null;
  topicId: string;
  topicTitle: string;
  bankId: string;
  attempts: number;
  correctCount: number;
  correctRate: number | null;
  discrimination: number | null;
  topGroupCorrectRate: number | null;
  bottomGroupCorrectRate: number | null;
  completedItems: number;
  skippedItems: number;
  skipRate: number | null;
  avgSecondsPerQuestion: number | null;
  timeSampleCount: number;
  openText: AdminOpenTextQuality | null;
}

/** Admin question-quality снимок за окно `days` (#681 T4). */
export interface AdminQuestionQualityStats {
  days: number;
  /** Верхняя отсечка для time-per-question приближения (секунды). */
  outlierCapSeconds: number;
  questions: AdminQuestionQualityItem[];
}

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats/topics — per-topic / per-bank + calibration (#681 T5)
// ─────────────────────────────────────────────────────────────────────────────

/** Срез по одной теме: mastery (снимок) + %-верных и объём за окно. */
export interface AdminTopicStat {
  topicId: string;
  topicTitle: string;
  avgMasteryPercent: number;
  masteryUsers: number;
  avgCorrectPercent: number;
  answersCount: number;
  sessionsCount: number;
}

/** Сколько вопросов банка одного типа. */
export interface AdminQuestionTypeCount {
  type: string;
  count: number;
}

/** Сколько вопросов банка одной сложности (`UNSPECIFIED` — без заявленной). */
export interface AdminQuestionDifficultyCount {
  difficulty: string;
  count: number;
}

/** Срез по одному банку вопросов: покрытие + состав по типу и сложности. */
export interface AdminBankStat {
  bankId: string;
  topicId: string;
  topicTitle: string;
  tier: string;
  difficulty: string | null;
  purpose: string;
  totalQuestions: number;
  answeredQuestions: number;
  coveragePercent: number;
  answersCount: number;
  byType: AdminQuestionTypeCount[];
  byDifficulty: AdminQuestionDifficultyCount[];
}

/** Фактический %-верных + объём по одному заявленному уровню сложности. */
export interface AdminCalibrationLevel {
  difficulty: string;
  actualCorrectPercent: number;
  answersCount: number;
  questionsAnswered: number;
}

/** Вопрос-выброс калибровки: фактический %-верных и отклонение от среднего уровня. */
export interface AdminMiscalibratedQuestion {
  questionId: string;
  bankId: string;
  topicId: string;
  topicTitle: string;
  difficulty: string;
  stem: string;
  actualCorrectPercent: number;
  deltaVsLevel: number;
  answersCount: number;
}

/** Калибровка сложности: заявленный уровень vs фактический %-верных + выбросы. */
export interface AdminDifficultyCalibration {
  levels: AdminCalibrationLevel[];
  miscalibrated: AdminMiscalibratedQuestion[];
}

/** Контент-аналитика тренажёра: per-topic / per-bank + калибровка (#681 T5). */
export interface AdminTopicBankStats {
  days: number;
  topics: AdminTopicStat[];
  banks: AdminBankStat[];
  calibration: AdminDifficultyCalibration;
}

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats/trends — dense daily owner-curve (#681 T6)
// ─────────────────────────────────────────────────────────────────────────────

/** Один день owner-кривой: платформенные KPI за день (zero-filled). */
export interface AdminTrendPoint {
  /** ISO date (`YYYY-MM-DD`). */
  date: string;
  sessionsStarted: number;
  activeUsers: number;
  completedSessions: number;
  costMicroRub: number;
  costRub: number;
  avgAccuracyPct: number;
}

/** Owner-кривая динамики тренажёра по дням за окно `days` (#681 T6). */
export interface AdminTrendStats {
  days: number;
  points: AdminTrendPoint[];
}

// ─────────────────────────────────────────────────────────────────────────────
// GET /trainer/admin/stats/feedback-ratings — 👍/👎 AI-разбора по вопросам (#691 t7)
// ─────────────────────────────────────────────────────────────────────────────

/** Оценки AI-разбора («Разбор ИИ») одного вопроса за окно. */
export interface AdminFeedbackRatingItem {
  questionId: string;
  /** Текст вопроса (для показа в таблице). */
  stem: string;
  /** Тип вопроса (как правило OPEN_TEXT — только у них есть AI-разбор). */
  questionType: string;
  difficulty: string | null;
  topicId: string;
  topicTitle: string;
  bankId: string;
  /** Сколько 👍. */
  up: number;
  /** Сколько 👎. */
  down: number;
  /** Всего оценок (`up` + `down`). */
  total: number;
  /** Доля 👎 = `down` / `total` (0..1). */
  downRate: number;
}

/**
 * Admin-агрегат оценок AI-разбора за окно `days` (#691 t7): по-вопросная разбивка 👍/👎,
 * упорядочено «худшие сверху» (наибольший down-rate) — кандидаты на правку промпта/эталона.
 */
export interface AdminFeedbackRatingStats {
  days: number;
  questions: AdminFeedbackRatingItem[];
}
