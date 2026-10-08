import { describe, expect, it } from "vitest";
import { getAdjacentIssues, getAdjacentLearningItems } from "../learning-navigation";
import type { CourseCurriculumDto } from "../../types";

function curriculum(
  sections: Array<{
    id: string;
    itemType: "Module" | "Project";
    title: string;
    items: Array<{ id: string; itemType: "Material" | "Issue" | "Quiz"; title: string }>;
  }>,
): CourseCurriculumDto {
  return {
    id: "c1",
    authorId: "a1",
    slug: "course",
    title: "Course",
    description: "",
    status: "PUBLISHED",
    kind: "COURSE",
    imageId: null,
    imageUrl: null,
    gettingStartedModuleId: null,
    hasFreeContent: false,
    isNew: false,
    createdAt: "",
    updatedAt: "",
    sections: sections.map((s) => ({
      id: s.id,
      itemType: s.itemType,
      title: s.title,
      description: null,
      detailedDescription: null,
      sortKey: "a",
      isOptional: false,
      items: s.items.map((i, idx) => ({
        id: i.id,
        itemType: i.itemType,
        title: i.title,
        sortKey: `a${idx}`,
        isOptional: false,
        position: idx,
        accessType: null,
        viewPriority: null,
        materialKind: null,
        durationSeconds: null,
        coverUrl: null,
      })),
    })),
  };
}

describe("getAdjacentIssues", () => {
  it("returns prev/next across the full issues stream regardless of section type", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [
          { id: "mat1", itemType: "Material", title: "Mat1" },
          { id: "iss1", itemType: "Issue", title: "Iss1" },
        ],
      },
      {
        id: "p1",
        itemType: "Project",
        title: "P1",
        items: [
          { id: "iss2", itemType: "Issue", title: "Iss2" },
          { id: "iss3", itemType: "Issue", title: "Iss3" },
        ],
      },
    ]);
    const nav = getAdjacentIssues(c, "iss2");
    expect(nav.previousItem?.id).toBe("iss1");
    expect(nav.nextItem?.id).toBe("iss3");
  });

  it("dedupes issues that appear in both a Module and a Project section", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [
          { id: "iss1", itemType: "Issue", title: "Iss1" },
          { id: "iss2", itemType: "Issue", title: "Iss2" },
        ],
      },
      {
        id: "p1",
        itemType: "Project",
        title: "P1",
        items: [{ id: "iss2", itemType: "Issue", title: "Iss2" }],
      },
    ]);
    const nav = getAdjacentIssues(c, "iss1");
    expect(nav.nextItem?.id).toBe("iss2");
    expect(nav.items).toHaveLength(2);
  });

  it("returns null when current issue is the first/last", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [
          { id: "iss1", itemType: "Issue", title: "Iss1" },
          { id: "iss2", itemType: "Issue", title: "Iss2" },
        ],
      },
    ]);
    expect(getAdjacentIssues(c, "iss1").previousItem).toBeNull();
    expect(getAdjacentIssues(c, "iss2").nextItem).toBeNull();
  });
});

describe("getAdjacentLearningItems", () => {
  it("walks across all Module items when current is in a Module section", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [
          { id: "mat1", itemType: "Material", title: "Mat1" },
          { id: "iss1", itemType: "Issue", title: "Iss1" },
        ],
      },
      {
        id: "m2",
        itemType: "Module",
        title: "M2",
        items: [{ id: "mat2", itemType: "Material", title: "Mat2" }],
      },
    ]);
    const nav = getAdjacentLearningItems(c, "iss1");
    expect(nav.previousItem?.id).toBe("mat1");
    expect(nav.nextItem?.id).toBe("mat2");
  });

  it("includes quizzes in the program order so neighbours don't skip them", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [
          { id: "mat1", itemType: "Material", title: "Guide" },
          { id: "quiz1", itemType: "Quiz", title: "Self-check" },
          { id: "iss1", itemType: "Issue", title: "Iss1" },
        ],
      },
    ]);
    // The issue right after a quiz must step back onto the quiz, not over it.
    const fromIssue = getAdjacentLearningItems(c, "iss1");
    expect(fromIssue.previousItem?.id).toBe("quiz1");
    // The quiz itself now has neighbours → its page can render a footer.
    const fromQuiz = getAdjacentLearningItems(c, "quiz1");
    expect(fromQuiz.previousItem?.id).toBe("mat1");
    expect(fromQuiz.nextItem?.id).toBe("iss1");
  });

  it("keeps quizzes out of the issues-only stream", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [
          { id: "iss1", itemType: "Issue", title: "Iss1" },
          { id: "quiz1", itemType: "Quiz", title: "Self-check" },
          { id: "iss2", itemType: "Issue", title: "Iss2" },
        ],
      },
    ]);
    const nav = getAdjacentIssues(c, "iss1");
    expect(nav.nextItem?.id).toBe("iss2");
    expect(nav.items.map((i) => i.id)).toEqual(["iss1", "iss2"]);
  });

  it("stays within the same Project section when current is in a Project", () => {
    const c = curriculum([
      {
        id: "m1",
        itemType: "Module",
        title: "M1",
        items: [{ id: "mat1", itemType: "Material", title: "Mat1" }],
      },
      {
        id: "p1",
        itemType: "Project",
        title: "P1",
        items: [
          { id: "iss1", itemType: "Issue", title: "Iss1" },
          { id: "iss2", itemType: "Issue", title: "Iss2" },
        ],
      },
    ]);
    const nav = getAdjacentLearningItems(c, "iss1");
    expect(nav.previousItem).toBeNull();
    expect(nav.nextItem?.id).toBe("iss2");
  });
});
