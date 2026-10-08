/**
 * Типы мок-собеседований тренажёра — зеркалят C#-контракт
 * `TrainerService.Contracts.MockInterviews`. Свойства camelCase.
 * Issue #568, конструктор — #585.
 *
 * Мок-собес — это POSITION-ориентированная кураторская подборка вопросов
 * (не привязка к треку). Метаданные не gated — список доступен любому
 * залогиненному (требует `content.view`). Конструктор (manage/builder/
 * question-bank) — admin-only.
 */

import type { QuizDifficultyLevel, QuizQuestionType } from "@/entities/quiz";

/**
 * Карточка мок-собеседования в студенческом селекторе вкладки «Симуляция»:
 * метаданные + размеры подборки. Зеркало `MockInterviewSummaryDto`.
 */
export interface MockInterviewSummary {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  /** Сколько тем входит в подборку мок-собеса (legacy topic-scope). */
  topicCount: number;
  /** Эффективное число вопросов на сессию (подвыборка либо размер набора). */
  questionCount: number;
}

/**
 * Строка авторского списка мок-собесов (включая DRAFT): метаданные + размеры
 * набора. Зеркало `MockInterviewManageItemDto`. #585.
 */
export interface MockInterviewManageItem {
  id: string;
  slug: string;
  title: string;
  isPublished: boolean;
  /** Размер курированного набора (M в «N из M»). */
  questionCount: number;
  /** Случайная подвыборка на сессию (N); null = показывать весь набор. */
  questionsPerSession: number | null;
}

/**
 * Один вопрос курированного набора в редакторе автора: ссылка (questionId) +
 * резолвнутые из локального банка метаданные + тема-источник. Зеркало
 * `MockInterviewBuilderQuestionDto`. #585/#623.
 */
export interface MockInterviewBuilderQuestion {
  questionId: string;
  text: string;
  type: QuizQuestionType;
  difficulty: QuizDifficultyLevel | null;
  topicId: string | null;
  topicTitle: string | null;
}

/**
 * Детальная карточка мок-собеса для редактора: метаданные + размер подвыборки +
 * курированный набор (с резолвнутыми стемами, в порядке SortIndex). Зеркало
 * `MockInterviewBuilderDto`. #585.
 */
export interface MockInterviewBuilderDto {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  isPublished: boolean;
  questionsPerSession: number | null;
  questions: MockInterviewBuilderQuestion[];
}

/**
 * Один доступный для выбора вопрос в пикере банка (источник курированного
 * набора): ссылка (questionId) + банк-источник + метаданные + тема/трек-источник.
 * Зеркало `QuestionBankItemDto`. #585/#623.
 */
export interface QuestionBankItem {
  questionId: string;
  text: string;
  type: QuizQuestionType;
  difficulty: QuizDifficultyLevel | null;
  bankId: string;
  topicId: string;
  topicTitle: string;
  trackId: string;
  trackTitle: string;
}

/** Ссылка на конкретный вопрос локального банка тренажёра в курированном наборе. Зеркало `MockInterviewQuestionRefDto` (#623). */
export interface MockInterviewQuestionRef {
  questionId: string;
}

/** Тело запроса на создание мок-собеса (admin). Slug обязателен на бэкенде. */
export interface CreateMockInterviewBody {
  slug: string;
  title: string;
  description?: string | null;
  topicIds?: string[];
}

/**
 * Тело PUT-обновления мок-собеса (admin): метаданные + размер подвыборки +
 * полная замена курированного набора. `questionsPerSession = null` →
 * показывать весь набор. Зеркало `UpdateMockInterviewRequest`. #585.
 */
export interface UpdateMockInterviewBody {
  title: string;
  description?: string | null;
  questionsPerSession?: number | null;
  questions: MockInterviewQuestionRef[];
}

/** Фильтр пикера банка вопросов: трек + тема (оба опциональны). */
export interface QuestionBankFilter {
  trackId?: string;
  topicId?: string;
}
