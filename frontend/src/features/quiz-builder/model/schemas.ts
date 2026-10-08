import {
  QUIZ_DIFFICULTY_LEVELS,
  QUIZ_EXPLANATION_MAX_LENGTH,
  QUIZ_MAX_OPTIONS,
  QUIZ_MAX_QUESTIONS,
  QUIZ_MIN_OPTIONS,
  QUIZ_OPTION_TEXT_MAX_LENGTH,
  QUIZ_QUESTION_TEXT_MAX_LENGTH,
  QUIZ_QUESTION_TYPES,
  QUIZ_REFERENCE_ANSWER_MAX_LENGTH,
  QUIZ_TITLE_MAX_LENGTH,
  type QuizAuthorDto,
  type QuizDifficultyLevel,
  type QuizQuestionRequest,
  type QuizQuestionType,
} from "@/entities/quiz";
import { z } from "zod";

/**
 * Черновик вопроса в билдере. `id` генерирует UI (crypto.randomUUID).
 * `section`/`difficulty` — level-test метаданные (#487); в material-билдере
 * всегда `null` и в UI не показываются.
 */
export interface QuizQuestionDraft {
  id: string;
  type: QuizQuestionType;
  text: string;
  options: { id: string; text: string }[];
  correctOptionIds: string[];
  referenceAnswer: string;
  /** Пояснение к вопросу — студент видит его в разборе после ответа (#561). */
  explanation: string;
  section: string | null;
  difficulty: QuizDifficultyLevel | null;
}

const quizOptionDraftSchema = z.object({
  id: z.string(),
  text: z
    .string()
    .trim()
    .min(1, "Введите текст варианта")
    .max(QUIZ_OPTION_TEXT_MAX_LENGTH, `Максимум ${QUIZ_OPTION_TEXT_MAX_LENGTH} символов`),
});

/**
 * Зеркало доменных инвариантов `QuizQuestion` (ECS): choice — 2..10 вариантов,
 * SINGLE — ровно один правильный, MULTI — хотя бы один; OPEN_TEXT — без вариантов,
 * эталон опционален (до 4000 символов); EXACT_TEXT (#528) — без вариантов,
 * эталонный точный ответ обязателен.
 */
export const quizQuestionDraftSchema = z
  .object({
    id: z.string(),
    type: z.enum(QUIZ_QUESTION_TYPES),
    text: z
      .string()
      .trim()
      .min(1, "Введите текст вопроса")
      .max(QUIZ_QUESTION_TEXT_MAX_LENGTH, `Максимум ${QUIZ_QUESTION_TEXT_MAX_LENGTH} символов`),
    options: z.array(quizOptionDraftSchema),
    correctOptionIds: z.array(z.string()),
    referenceAnswer: z
      .string()
      .trim()
      .max(
        QUIZ_REFERENCE_ANSWER_MAX_LENGTH,
        `Максимум ${QUIZ_REFERENCE_ANSWER_MAX_LENGTH} символов`,
      ),
    explanation: z
      .string()
      .trim()
      .max(QUIZ_EXPLANATION_MAX_LENGTH, `Максимум ${QUIZ_EXPLANATION_MAX_LENGTH} символов`)
      .default(""),
    section: z.string().nullable(),
    difficulty: z.enum(QUIZ_DIFFICULTY_LEVELS).nullable(),
  })
  .superRefine((question, ctx) => {
    if (question.type === "EXACT_TEXT") {
      if (question.referenceAnswer.trim().length === 0) {
        ctx.addIssue({
          code: "custom",
          path: ["referenceAnswer"],
          message: "Введите эталонный точный ответ",
        });
      }
      return;
    }

    if (question.type === "OPEN_TEXT") return;

    if (question.options.length < QUIZ_MIN_OPTIONS || question.options.length > QUIZ_MAX_OPTIONS) {
      ctx.addIssue({
        code: "custom",
        path: ["options"],
        message: `От ${QUIZ_MIN_OPTIONS} до ${QUIZ_MAX_OPTIONS} вариантов ответа`,
      });
    }

    if (question.type === "SINGLE_CHOICE" && question.correctOptionIds.length !== 1) {
      ctx.addIssue({
        code: "custom",
        path: ["correctOptionIds"],
        message: "Отметьте ровно один правильный вариант",
      });
    }

    if (question.type === "MULTI_CHOICE" && question.correctOptionIds.length < 1) {
      ctx.addIssue({
        code: "custom",
        path: ["correctOptionIds"],
        message: "Отметьте хотя бы один правильный вариант",
      });
    }
  });

export const quizBuilderSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, "Введите название теста")
    .max(QUIZ_TITLE_MAX_LENGTH, `Максимум ${QUIZ_TITLE_MAX_LENGTH} символов`),
  passingScorePercent: z
    .number("Проходной балл — число от 0 до 100")
    .int("Проходной балл — целое число")
    .min(0, "Минимум 0")
    .max(100, "Максимум 100"),
  questions: z
    .array(quizQuestionDraftSchema)
    .max(QUIZ_MAX_QUESTIONS, `Не больше ${QUIZ_MAX_QUESTIONS} вопросов`),
});

export type QuizBuilderValues = z.infer<typeof quizBuilderSchema>;

export function createQuizOptionDraft(): { id: string; text: string } {
  return { id: crypto.randomUUID(), text: "" };
}

export function createQuizQuestionDraft(): QuizQuestionDraft {
  return {
    id: crypto.randomUUID(),
    type: "SINGLE_CHOICE",
    text: "",
    options: [createQuizOptionDraft(), createQuizOptionDraft()],
    correctOptionIds: [],
    referenceAnswer: "",
    explanation: "",
    section: null,
    difficulty: null,
  };
}

/** Серверная авторская проекция → черновики вопросов билдера. */
export function toQuestionDrafts(quiz: QuizAuthorDto): QuizQuestionDraft[] {
  return quiz.questions.map((question) => ({
    id: question.id,
    type: question.type,
    text: question.text,
    options: question.options.map((option) => ({ id: option.id, text: option.text })),
    correctOptionIds: [...question.correctOptionIds],
    referenceAnswer: question.referenceAnswer ?? "",
    explanation: question.explanation ?? "",
    section: question.section,
    difficulty: question.difficulty,
  }));
}

/**
 * Валидированный черновик вопроса → контрактный `QuizQuestionRequest`.
 * Общий маппер material-билдера и level-test редактора: текстовые вопросы
 * (OPEN_TEXT и EXACT_TEXT, #540) — без вариантов, с эталоном (у OPEN пустой →
 * `null`, у EXACT непустой гарантирует zod-валидация); choice — без эталона.
 */
export function toQuestionRequest(
  question: QuizBuilderValues["questions"][number],
): QuizQuestionRequest {
  const explanation = (question.explanation ?? "").length > 0 ? question.explanation! : null;
  const levelTestMeta = { explanation, section: question.section, difficulty: question.difficulty };
  if (question.type === "OPEN_TEXT" || question.type === "EXACT_TEXT") {
    return {
      id: question.id,
      type: question.type,
      text: question.text,
      options: [],
      correctOptionIds: [],
      referenceAnswer: question.referenceAnswer.length > 0 ? question.referenceAnswer : null,
      ...levelTestMeta,
    };
  }
  return {
    id: question.id,
    type: question.type,
    text: question.text,
    options: question.options.map((option) => ({ id: option.id, text: option.text })),
    correctOptionIds: question.correctOptionIds,
    referenceAnswer: null,
    ...levelTestMeta,
  };
}

/** Плоская карта ошибок `path.join(".") → message` для inline-рендера в билдере. */
export function flattenQuizBuilderIssues(error: z.ZodError): Record<string, string> {
  const flat: Record<string, string> = {};
  for (const issue of error.issues) {
    const key = issue.path.join(".");
    if (!(key in flat)) flat[key] = issue.message;
  }
  return flat;
}
