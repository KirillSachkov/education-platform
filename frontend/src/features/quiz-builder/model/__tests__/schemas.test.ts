import { describe, expect, it } from "vitest";
import {
  createQuizQuestionDraft,
  flattenQuizBuilderIssues,
  quizBuilderSchema,
  toQuestionRequest,
  type QuizQuestionDraft,
} from "../schemas";

function buildSingleChoice(overrides: Partial<QuizQuestionDraft> = {}): QuizQuestionDraft {
  return {
    id: "q1",
    type: "SINGLE_CHOICE",
    text: "Что такое CLR?",
    options: [
      { id: "o1", text: "Среда выполнения" },
      { id: "o2", text: "Компилятор" },
    ],
    correctOptionIds: ["o1"],
    referenceAnswer: "",
    explanation: "",
    section: null,
    difficulty: null,
    ...overrides,
  };
}

function buildValues(questions: QuizQuestionDraft[]) {
  return { title: "Квиз по основам", passingScorePercent: 70, questions };
}

describe("quizBuilderSchema", () => {
  it("accepts a valid quiz with all three question types", () => {
    const result = quizBuilderSchema.safeParse(
      buildValues([
        buildSingleChoice(),
        buildSingleChoice({
          id: "q2",
          type: "MULTI_CHOICE",
          options: [
            { id: "m1", text: "string" },
            { id: "m2", text: "int" },
            { id: "m3", text: "object" },
          ],
          correctOptionIds: ["m1", "m3"],
        }),
        {
          id: "q3",
          type: "OPEN_TEXT",
          text: "Объясните разницу между class и struct",
          options: [],
          correctOptionIds: [],
          referenceAnswer: "Эталон",
          explanation: "",
          section: null,
          difficulty: null,
        },
      ]),
    );
    expect(result.success).toBe(true);
  });

  it("rejects SINGLE_CHOICE without exactly one correct option", () => {
    const result = quizBuilderSchema.safeParse(
      buildValues([buildSingleChoice({ correctOptionIds: ["o1", "o2"] })]),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["questions.0.correctOptionIds"]).toBeTruthy();
  });

  it("rejects MULTI_CHOICE without correct options", () => {
    const result = quizBuilderSchema.safeParse(
      buildValues([buildSingleChoice({ type: "MULTI_CHOICE", correctOptionIds: [] })]),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["questions.0.correctOptionIds"]).toBeTruthy();
  });

  it("rejects choice question with fewer than two options", () => {
    const result = quizBuilderSchema.safeParse(
      buildValues([
        buildSingleChoice({ options: [{ id: "o1", text: "Один" }], correctOptionIds: ["o1"] }),
      ]),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["questions.0.options"]).toBeTruthy();
  });

  it("rejects empty option text with per-option path", () => {
    const result = quizBuilderSchema.safeParse(
      buildValues([
        buildSingleChoice({
          options: [
            { id: "o1", text: "Заполнен" },
            { id: "o2", text: "  " },
          ],
        }),
      ]),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["questions.0.options.1.text"]).toBeTruthy();
  });

  it("rejects passing score outside 0..100 and NaN", () => {
    for (const passingScorePercent of [-1, 101, Number.NaN]) {
      const result = quizBuilderSchema.safeParse({
        ...buildValues([buildSingleChoice()]),
        passingScorePercent,
      });
      expect(result.success).toBe(false);
      const flat = flattenQuizBuilderIssues(result.error!);
      expect(flat.passingScorePercent).toBeTruthy();
    }
  });

  it("accepts OPEN_TEXT question without reference answer", () => {
    const draft = createQuizQuestionDraft();
    const result = quizBuilderSchema.safeParse(
      buildValues([
        {
          ...draft,
          type: "OPEN_TEXT",
          text: "Открытый вопрос",
          options: [],
          correctOptionIds: [],
          referenceAnswer: "",
        },
      ]),
    );
    expect(result.success).toBe(true);
  });
});

describe("toQuestionRequest — EXACT_TEXT (#540)", () => {
  it("keeps referenceAnswer for exact questions in the save payload", () => {
    const request = toQuestionRequest({
      id: "q-exact",
      type: "EXACT_TEXT",
      text: "Что выведет код?",
      options: [],
      correctOptionIds: [],
      referenceAnswer: "True False",
      explanation: "",
      section: "csharp-runtime",
      difficulty: "MIDDLE",
    });
    expect(request).toMatchObject({
      type: "EXACT_TEXT",
      options: [],
      correctOptionIds: [],
      referenceAnswer: "True False",
    });
  });
});
