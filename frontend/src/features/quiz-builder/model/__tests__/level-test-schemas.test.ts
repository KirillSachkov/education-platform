import { describe, expect, it } from "vitest";
import {
  buildLevelTestCreateRequest,
  LEVEL_TEST_BASE_MIN_PERCENT,
  LEVEL_TEST_DEFAULT_THRESHOLDS,
  levelTestBuilderSchema,
  levelTestSectionKeySchema,
  toLevelTestUpdateRequest,
  type LevelTestBuilderValues,
} from "../level-test-schemas";
import { flattenQuizBuilderIssues, type QuizQuestionDraft } from "../schemas";

function buildQuestion(overrides: Partial<QuizQuestionDraft> = {}): QuizQuestionDraft {
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
    section: "csharp-basics",
    difficulty: "JUNIOR",
    ...overrides,
  };
}

function buildValues(overrides: Partial<LevelTestBuilderValues> = {}): LevelTestBuilderValues {
  return {
    title: "Определи свой уровень",
    thresholds: { JUNIOR: 10, JUNIOR_PLUS: 26, MIDDLE: 44, MIDDLE_PLUS: 62, SENIOR: 80 },
    sections: [
      { key: "csharp-basics", title: "Основы C#", weight: 1, recommendedCourseId: null },
      { key: "async", title: "Асинхронность", weight: 2, recommendedCourseId: "course-1" },
    ],
    fallbackCourseId: "course-2",
    questions: [buildQuestion()],
    ...overrides,
  };
}

describe("levelTestSectionKeySchema", () => {
  it.each(["csharp-basics", "oop", "ef-sql", "a1-b2-c3"])("accepts kebab key '%s'", (key) => {
    expect(levelTestSectionKeySchema.safeParse(key).success).toBe(true);
  });

  it.each([
    "CSharp",
    "под-секция",
    "key_with_underscore",
    "-leading",
    "trailing-",
    "two--dashes",
    "",
  ])("rejects non-kebab key '%s'", (key) => {
    expect(levelTestSectionKeySchema.safeParse(key).success).toBe(false);
  });
});

describe("levelTestBuilderSchema", () => {
  it("accepts a valid level-test form", () => {
    expect(levelTestBuilderSchema.safeParse(buildValues()).success).toBe(true);
  });

  it("rejects thresholds that do not strictly ascend along the scale", () => {
    // MIDDLE_PLUS ниже MIDDLE — ошибка вешается на нарушивший уровень.
    const result = levelTestBuilderSchema.safeParse(
      buildValues({
        thresholds: { JUNIOR: 10, JUNIOR_PLUS: 26, MIDDLE: 44, MIDDLE_PLUS: 40, SENIOR: 80 },
      }),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["thresholds.MIDDLE_PLUS"]).toContain("выше предыдущего");
  });

  it("rejects equal neighbour thresholds", () => {
    const result = levelTestBuilderSchema.safeParse(
      buildValues({
        thresholds: { JUNIOR: 10, JUNIOR_PLUS: 10, MIDDLE: 44, MIDDLE_PLUS: 62, SENIOR: 80 },
      }),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["thresholds.JUNIOR_PLUS"]).toBeTruthy();
  });

  it("rejects thresholds outside 1..100, non-integer and NaN", () => {
    for (const middle of [0, 101, 45.5, Number.NaN]) {
      const result = levelTestBuilderSchema.safeParse(
        buildValues({
          thresholds: { JUNIOR: 10, JUNIOR_PLUS: 26, MIDDLE: middle, MIDDLE_PLUS: 62, SENIOR: 80 },
        }),
      );
      expect(result.success).toBe(false);
      const flat = flattenQuizBuilderIssues(result.error!);
      expect(flat["thresholds.MIDDLE"]).toBeTruthy();
    }
  });

  it("rejects duplicate section keys", () => {
    const result = levelTestBuilderSchema.safeParse(
      buildValues({
        sections: [
          { key: "oop", title: "ООП", weight: 1, recommendedCourseId: null },
          { key: "oop", title: "ООП дубль", weight: 1, recommendedCourseId: null },
        ],
      }),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat.sections).toContain("уникальными");
  });

  it("rejects non-positive section weight with per-section path", () => {
    const result = levelTestBuilderSchema.safeParse(
      buildValues({
        sections: [{ key: "oop", title: "ООП", weight: 0, recommendedCourseId: null }],
      }),
    );
    expect(result.success).toBe(false);
    const flat = flattenQuizBuilderIssues(result.error!);
    expect(flat["sections.0.weight"]).toBeTruthy();
  });

  it("accepts empty sections with no questions", () => {
    const result = levelTestBuilderSchema.safeParse(
      buildValues({ sections: [], fallbackCourseId: null, questions: [] }),
    );
    expect(result.success).toBe(true);
  });
});

describe("toLevelTestUpdateRequest", () => {
  it("maps form values to a full replace request with config and question metadata", () => {
    const values = buildValues({
      questions: [
        buildQuestion(),
        buildQuestion({
          id: "q2",
          type: "OPEN_TEXT",
          text: "Объясните async/await",
          options: [],
          correctOptionIds: [],
          referenceAnswer: "Эталон: … Критерии оценки: 1) … (~50 из 100)",
          section: "async",
          difficulty: "SENIOR",
        }),
      ],
    });

    const request = toLevelTestUpdateRequest(values, 70);

    expect(request.title).toBe("Определи свой уровень");
    expect(request.passingScorePercent).toBe(70);

    expect(request.levelTestConfig?.levelThresholds).toEqual([
      { level: "PRE_JUNIOR", minPercent: LEVEL_TEST_BASE_MIN_PERCENT },
      { level: "JUNIOR", minPercent: 10 },
      { level: "JUNIOR_PLUS", minPercent: 26 },
      { level: "MIDDLE", minPercent: 44 },
      { level: "MIDDLE_PLUS", minPercent: 62 },
      { level: "SENIOR", minPercent: 80 },
    ]);
    expect(request.levelTestConfig?.sections).toEqual([
      { key: "csharp-basics", title: "Основы C#", weight: 1, recommendedCourseId: null },
      { key: "async", title: "Асинхронность", weight: 2, recommendedCourseId: "course-1" },
    ]);
    expect(request.levelTestConfig?.fallbackCourseId).toBe("course-2");

    expect(request.questions).toHaveLength(2);
    const [choice, openText] = request.questions!;
    expect(choice.section).toBe("csharp-basics");
    expect(choice.difficulty).toBe("JUNIOR");
    expect(choice.referenceAnswer).toBeNull();
    expect(openText.section).toBe("async");
    expect(openText.difficulty).toBe("SENIOR");
    expect(openText.options).toEqual([]);
    expect(openText.referenceAnswer).toContain("Критерии оценки");
  });
});

describe("buildLevelTestCreateRequest", () => {
  it("creates a standalone LEVEL_TEST draft with default 6-level thresholds and empty sections", () => {
    const request = buildLevelTestCreateRequest();

    expect(request.purpose).toBe("LEVEL_TEST");
    expect(request.questions).toEqual([]);
    expect(request.levelTestConfig?.levelThresholds).toEqual([
      { level: "PRE_JUNIOR", minPercent: 0 },
      { level: "JUNIOR", minPercent: LEVEL_TEST_DEFAULT_THRESHOLDS.JUNIOR },
      { level: "JUNIOR_PLUS", minPercent: LEVEL_TEST_DEFAULT_THRESHOLDS.JUNIOR_PLUS },
      { level: "MIDDLE", minPercent: LEVEL_TEST_DEFAULT_THRESHOLDS.MIDDLE },
      { level: "MIDDLE_PLUS", minPercent: LEVEL_TEST_DEFAULT_THRESHOLDS.MIDDLE_PLUS },
      { level: "SENIOR", minPercent: LEVEL_TEST_DEFAULT_THRESHOLDS.SENIOR },
    ]);
    expect(request.levelTestConfig?.sections).toEqual([]);
    expect(request.levelTestConfig?.fallbackCourseId).toBeNull();
  });
});
