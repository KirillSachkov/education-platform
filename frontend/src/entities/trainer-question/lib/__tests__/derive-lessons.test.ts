import { describe, expect, it } from "vitest";
import type { TrainerQuestionListItem, TrainerStudyStatus } from "../../types";
import { deriveLessons, LESSON_SIZE } from "../derive-lessons";

function item(
  id: string,
  difficulty: string | null,
  status: TrainerStudyStatus = "NEW",
): TrainerQuestionListItem {
  return {
    questionId: id,
    stem: `Вопрос ${id}`,
    type: "SINGLE_CHOICE",
    difficulty,
    section: null,
    status,
    isBookmarked: false,
    isLocked: false,
    lockReason: null,
  };
}

describe("deriveLessons", () => {
  it("returns no lessons for an empty list", () => {
    expect(deriveLessons([])).toEqual([]);
  });

  it("orders levels JUNIOR → MIDDLE → SENIOR and skips empty levels", () => {
    const lessons = deriveLessons([
      item("s1", "SENIOR"),
      item("j1", "JUNIOR"),
      item("j2", "JUNIOR"),
    ]);
    expect(lessons.map((l) => l.level)).toEqual(["JUNIOR", "SENIOR"]);
    expect(lessons[0].title).toBe("Джуниор");
    expect(lessons[1].title).toBe("Сеньор");
  });

  it("chunks a level into numbered parts of LESSON_SIZE preserving order", () => {
    const items = Array.from({ length: LESSON_SIZE + 3 }, (_, i) =>
      item(`j${i}`, "JUNIOR"),
    );
    const lessons = deriveLessons(items);

    expect(lessons).toHaveLength(2);
    expect(lessons[0].title).toBe("Джуниор · Тест 1");
    expect(lessons[1].title).toBe("Джуниор · Тест 2");
    expect(lessons[0].questionIds).toHaveLength(LESSON_SIZE);
    expect(lessons[1].questionIds).toHaveLength(3);
    expect(lessons[0].partCount).toBe(2);
    // order preserved across the chunk boundary
    expect(lessons[1].questionIds[0]).toBe(`j${LESSON_SIZE}`);
  });

  it("uses a bare level label when there is a single part", () => {
    const lessons = deriveLessons([item("j1", "JUNIOR"), item("j2", "JUNIOR")]);
    expect(lessons).toHaveLength(1);
    expect(lessons[0].title).toBe("Джуниор");
    expect(lessons[0].partCount).toBe(1);
    expect(lessons[0].id).toBe("JUNIOR-0");
  });

  it("derives progress from status: known/attempted/isComplete", () => {
    const lessons = deriveLessons([
      item("j1", "JUNIOR", "KNOWN"),
      item("j2", "JUNIOR", "WRONG"),
      item("j3", "JUNIOR", "NEW"),
    ]);
    const lesson = lessons[0];
    expect(lesson.total).toBe(3);
    expect(lesson.known).toBe(1);
    expect(lesson.attempted).toBe(2); // KNOWN + WRONG, NEW excluded
    expect(lesson.isComplete).toBe(false);
  });

  it("marks a lesson complete only when every question is KNOWN", () => {
    const lessons = deriveLessons([
      item("j1", "JUNIOR", "KNOWN"),
      item("j2", "JUNIOR", "KNOWN"),
    ]);
    expect(lessons[0].isComplete).toBe(true);
    expect(lessons[0].known).toBe(2);
  });

  it("folds null-difficulty questions into a trailing «Без уровня» group", () => {
    const lessons = deriveLessons([
      item("u1", null),
      item("j1", "JUNIOR"),
      item("u2", "WEIRD_VALUE"),
    ]);
    expect(lessons.map((l) => l.level)).toEqual(["JUNIOR", "UNLEVELED"]);
    const unleveled = lessons[1];
    expect(unleveled.title).toBe("Без уровня");
    expect(unleveled.questionIds).toEqual(["u1", "u2"]);
  });
});
