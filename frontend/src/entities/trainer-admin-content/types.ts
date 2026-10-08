/**
 * Типы admin-проекций контента тренажёра (темы + банки) — зеркалят C#-контракты
 * `TrainerService.Contracts.Topics`. ASP.NET сериализует свойства camelCase,
 * enum'ы — строками UPPER_SNAKE_CASE. Питают админ-страницу `/author/trainer`
 * (CRUD тем и банков). Issue #623 (epic #568).
 */

/** Tier банка вопросов: FREE (любому залогиненному) | PAID (полный доступ). */
export const TRAINER_BANK_TIERS = ["FREE", "PAID"] as const;
export type TrainerBankTier = (typeof TRAINER_BANK_TIERS)[number];

/** Опциональная сложность банка вопросов. */
export const TRAINER_BANK_DIFFICULTIES = ["JUNIOR", "MIDDLE", "SENIOR"] as const;
export type TrainerBankDifficulty = (typeof TRAINER_BANK_DIFFICULTIES)[number];

/** Назначение банка: STUDY (изучение/тесты) | MOCK (симуляция собеса). */
export const TRAINER_BANK_PURPOSES = ["STUDY", "MOCK"] as const;
export type TrainerBankPurpose = (typeof TRAINER_BANK_PURPOSES)[number];

/** Facet направления темы в рамках трека (опц.). */
export const TRAINER_TOPIC_DIRECTIONS = ["BACKEND", "FRONTEND", "FULLSTACK", "GENERAL"] as const;
export type TrainerTopicDirection = (typeof TRAINER_TOPIC_DIRECTIONS)[number];

/**
 * Тема тренажёра в admin-проекции (включая DRAFT) — полные метаданные без
 * персонального mastery/фримиума. Зеркало `TopicAdminDto`.
 */
export interface TrainerTopicAdmin {
  id: string;
  trackId: string;
  slug: string;
  title: string;
  area: string;
  description: string | null;
  /** Направление (`BACKEND | FRONTEND | FULLSTACK | GENERAL`) либо null. */
  direction: string | null;
  recommendedCourseId: string | null;
  fallbackCourseId: string | null;
  sortKey: string;
  isPublished: boolean;
  /** Сколько банков вопросов привязано к теме (для гейта удаления). */
  bankCount: number;
  createdAt: string;
  updatedAt: string;
}

/**
 * Банк вопросов темы в admin builder-проекции — включает purpose + sortKey +
 * число вопросов (#623, банк собственный). Зеркало `TopicBankAdminDto`.
 */
export interface TrainerTopicBankAdmin {
  id: string;
  topicId: string;
  tier: string;
  difficulty: string | null;
  purpose: string;
  sortKey: string;
  /** Сколько вопросов залито в банк (#623). */
  questionCount: number;
  createdAt: string;
}

/** Тело запроса на создание темы (admin). Slug обязателен на бэкенде. */
export interface CreateTrainerTopicBody {
  trackId: string;
  slug: string;
  title: string;
  area: string;
  description?: string | null;
  direction?: string | null;
  recommendedCourseId?: string | null;
  fallbackCourseId?: string | null;
}

/**
 * Тело запроса на обновление темы (admin). Slug immutable — не передаётся.
 * `trackId` переназначает трек. Зеркало `UpdateTopicRequest`.
 */
export interface UpdateTrainerTopicBody {
  trackId: string;
  title: string;
  area: string;
  description?: string | null;
  direction?: string | null;
  recommendedCourseId?: string | null;
  fallbackCourseId?: string | null;
}

/**
 * Тело запроса на добавление банка вопросов к теме (admin). Банк создаётся ПУСТЫМ
 * (#623 — вопросы добавляются отдельно через question CRUD). Зеркало `AddTopicBankRequest`.
 */
export interface AddTrainerTopicBankBody {
  tier?: string | null;
  difficulty?: string | null;
}

/** Тело PUT-обновления банка (admin): tier/difficulty/purpose. TopicId immutable. */
export interface UpdateTrainerTopicBankBody {
  tier?: string | null;
  difficulty?: string | null;
  purpose?: string | null;
}
