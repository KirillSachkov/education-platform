import {
  QUIZ_DEFAULT_PASSING_SCORE_PERCENT,
  QUIZ_MAX_QUESTIONS,
  QUIZ_SECTION_KEY_MAX_LENGTH,
  QUIZ_TITLE_MAX_LENGTH,
  type CreateQuizRequest,
  type UpdateQuizRequest,
} from "@/entities/quiz";
import type { DeveloperLevel } from "@/shared/types";
import { z } from "zod";
import { quizQuestionDraftSchema, toQuestionRequest } from "./schemas";

/**
 * Модель авторского редактора level-test'а (#487): zod-зеркало серверных
 * инвариантов `LevelTestConfig` + маппинг формы в контрактные запросы.
 */

/** Порог базового уровня фиксирован: «Новичок» — от 0%. */
export const LEVEL_TEST_BASE_MIN_PERCENT = 0;

/**
 * Редактируемые уровни 6-ступенчатой шкалы (#528) в порядке возрастания —
 * PRE_JUNIOR фиксирован на 0% и в форму не входит.
 */
export const LEVEL_TEST_EDITABLE_LEVELS = [
  "JUNIOR",
  "JUNIOR_PLUS",
  "MIDDLE",
  "MIDDLE_PLUS",
  "SENIOR",
] as const;

export type LevelTestEditableLevel = (typeof LEVEL_TEST_EDITABLE_LEVELS)[number];

/** Дефолтные пороги нового level-test'а — синхронны с SeedData/level-test.json. */
export const LEVEL_TEST_DEFAULT_THRESHOLDS: Record<LevelTestEditableLevel, number> = {
  JUNIOR: 25,
  JUNIOR_PLUS: 50,
  MIDDLE: 65,
  MIDDLE_PLUS: 78,
  SENIOR: 95,
};

const SECTION_KEY_REGEX = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** Kebab-ключ секции: латиница/цифры через дефис (`csharp-basics`). */
export const levelTestSectionKeySchema = z
  .string()
  .trim()
  .min(1, "Введите ключ секции")
  .max(QUIZ_SECTION_KEY_MAX_LENGTH, `Максимум ${QUIZ_SECTION_KEY_MAX_LENGTH} символов`)
  .regex(SECTION_KEY_REGEX, "Ключ — kebab-case: строчная латиница, цифры и дефисы");

const levelTestSectionDraftSchema = z.object({
  key: levelTestSectionKeySchema,
  title: z
    .string()
    .trim()
    .min(1, "Введите название секции")
    .max(QUIZ_TITLE_MAX_LENGTH, `Максимум ${QUIZ_TITLE_MAX_LENGTH} символов`),
  weight: z.number("Вес — положительное число").positive("Вес — положительное число"),
  recommendedCourseId: z.string().nullable(),
});

const thresholdPercentSchema = z
  .number("Порог — целое число от 1 до 100")
  .int("Порог — целое число")
  .min(1, "Минимум 1")
  .max(100, "Максимум 100");

export const levelTestBuilderSchema = z
  .object({
    title: z
      .string()
      .trim()
      .min(1, "Введите название теста")
      .max(QUIZ_TITLE_MAX_LENGTH, `Максимум ${QUIZ_TITLE_MAX_LENGTH} символов`),
    thresholds: z.object({
      JUNIOR: thresholdPercentSchema,
      JUNIOR_PLUS: thresholdPercentSchema,
      MIDDLE: thresholdPercentSchema,
      MIDDLE_PLUS: thresholdPercentSchema,
      SENIOR: thresholdPercentSchema,
    }),
    sections: z.array(levelTestSectionDraftSchema),
    fallbackCourseId: z.string().nullable(),
    questions: z
      .array(quizQuestionDraftSchema)
      .max(QUIZ_MAX_QUESTIONS, `Не больше ${QUIZ_MAX_QUESTIONS} вопросов`),
  })
  .superRefine((values, ctx) => {
    // Пороги обязаны строго расти вдоль шкалы — иначе верхний уровень недостижим.
    for (let i = 1; i < LEVEL_TEST_EDITABLE_LEVELS.length; i++) {
      const prev = LEVEL_TEST_EDITABLE_LEVELS[i - 1];
      const current = LEVEL_TEST_EDITABLE_LEVELS[i];
      if (values.thresholds[current] <= values.thresholds[prev]) {
        ctx.addIssue({
          code: "custom",
          path: ["thresholds", current],
          message: "Порог должен быть выше предыдущего уровня",
        });
      }
    }

    const keys = values.sections.map((section) => section.key);
    if (new Set(keys).size !== keys.length) {
      ctx.addIssue({
        code: "custom",
        path: ["sections"],
        message: "Ключи секций должны быть уникальными",
      });
    }
  });

export type LevelTestBuilderValues = z.infer<typeof levelTestBuilderSchema>;

/** Пороги формы → контрактный массив: фиксированный PRE_JUNIOR 0 + 5 редактируемых. */
function toThresholdRequests(
  thresholds: Record<LevelTestEditableLevel, number>,
): { level: DeveloperLevel; minPercent: number }[] {
  return [
    { level: "PRE_JUNIOR", minPercent: LEVEL_TEST_BASE_MIN_PERCENT },
    ...LEVEL_TEST_EDITABLE_LEVELS.map((level) => ({
      level,
      minPercent: thresholds[level],
    })),
  ];
}

/**
 * Валидированная форма → `PUT /quizzes/{id}/` (replace целиком: title + вопросы с
 * section/difficulty + конфиг с порогами 6-ступенчатой шкалы, PRE_JUNIOR — от 0%).
 * `passingScorePercent` для level-test'а не редактируется — прокидывается текущее
 * значение квиза (уровень определяют пороги, не проходной балл).
 */
export function toLevelTestUpdateRequest(
  values: LevelTestBuilderValues,
  passingScorePercent: number,
): UpdateQuizRequest {
  return {
    title: values.title,
    passingScorePercent,
    questions: values.questions.map(toQuestionRequest),
    levelTestConfig: {
      levelThresholds: toThresholdRequests(values.thresholds),
      sections: values.sections.map((section) => ({
        key: section.key,
        title: section.title,
        weight: section.weight,
        recommendedCourseId: section.recommendedCourseId,
      })),
      fallbackCourseId: values.fallbackCourseId,
    },
  };
}

/**
 * `POST /quizzes/` для CTA «Создать тест уровня»: DRAFT level-test без вопросов,
 * дефолтные пороги 6-ступенчатой шкалы (см. LEVEL_TEST_DEFAULT_THRESHOLDS),
 * пустые секции — автор наполняет их в редакторе.
 */
export function buildLevelTestCreateRequest(): CreateQuizRequest {
  return {
    title: "Тест уровня",
    questions: [],
    passingScorePercent: QUIZ_DEFAULT_PASSING_SCORE_PERCENT,
    purpose: "LEVEL_TEST",
    levelTestConfig: {
      levelThresholds: toThresholdRequests(LEVEL_TEST_DEFAULT_THRESHOLDS),
      sections: [],
      fallbackCourseId: null,
    },
  };
}
