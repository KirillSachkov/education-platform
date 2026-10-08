/**
 * Типы admin-CRUD вопросов собственного банка тренажёра (#623) — зеркалят
 * C#-контракты `TrainerService.Contracts.Questions` (admin-проекции). Свойства
 * camelCase, enum'ы строками UPPER_SNAKE_CASE.
 *
 * Admin-only: эти DTO несут ключ грейдинга (`isCorrect` / `referenceAnswer`) и
 * НИКОГДА не отдаются студентам — только в редакторе вопросов банка.
 */

import type { QuizQuestionType } from "@/entities/quiz";

/** Тип вопроса банка тренажёра — тот же набор, что у квизов платформы. */
export type TrainerQuestionType = QuizQuestionType;

/** Сложность вопроса (опц.). */
export const TRAINER_QUESTION_DIFFICULTIES = ["JUNIOR", "MIDDLE", "SENIOR"] as const;
export type TrainerQuestionDifficulty = (typeof TRAINER_QUESTION_DIFFICULTIES)[number];

/**
 * Вариант ответа в admin-проекции вопроса (редактор): включает `isCorrect`.
 * Зеркало `QuestionOptionAdminDto`. Никогда не отдаётся студентам.
 */
export interface TrainerQuestionOptionAdmin {
  id: string;
  text: string;
  isCorrect: boolean;
  sortIndex: number;
}

/**
 * Полный вопрос банка (admin builder-проекция) — для редактора: варианты с
 * признаком правильности + эталон + разбор + sortKey. Зеркало `QuestionAdminDto`.
 */
export interface TrainerQuestionAdmin {
  id: string;
  bankId: string;
  stem: string;
  type: TrainerQuestionType;
  referenceAnswer: string | null;
  explanation: string | null;
  difficulty: string | null;
  section: string | null;
  sortKey: string;
  options: TrainerQuestionOptionAdmin[];
  createdAt: string;
  updatedAt: string;
}

/** Вариант ответа в запросе создания/обновления (только для choice-типов). Зеркало `QuestionOptionInputDto`. */
export interface TrainerQuestionOptionInput {
  text: string;
  isCorrect: boolean;
}

/**
 * Тело запроса на создание/обновление вопроса банка (admin). Зеркало
 * `QuestionInputDto`. Для choice-типов нужны `options` (≥2, с признаком
 * правильности); для EXACT_TEXT — `referenceAnswer`.
 */
export interface TrainerQuestionInput {
  stem: string | null;
  type: TrainerQuestionType;
  referenceAnswer?: string | null;
  explanation?: string | null;
  difficulty?: string | null;
  section?: string | null;
  options?: TrainerQuestionOptionInput[] | null;
}
