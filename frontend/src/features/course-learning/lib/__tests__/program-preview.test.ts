import { describe, expect, it } from "vitest";
import type { CurriculumSectionDto } from "@/entities/course";
import type { CourseLearningStateDto } from "@/entities/course-progress";
import { selectProgramPreviewSections } from "../program-preview";

function section(id: string, items: Array<{ id: string; itemType: string }>): CurriculumSectionDto {
  return {
    id,
    itemType: "Module",
    title: `Модуль ${id}`,
    description: null,
    detailedDescription: null,
    sortKey: id,
    isOptional: false,
    items: items.map((item, position) => ({
      id: item.id,
      itemType: item.itemType,
      title: `Item ${item.id}`,
      sortKey: `${id}-${position}`,
      isOptional: false,
      position: position + 1,
      accessType: "ENROLLED",
      viewPriority: null,
      materialKind: item.itemType === "Material" ? "VIDEO" : null,
      durationSeconds: null,
      coverUrl: null,
    })),
  };
}

function project(id: string): CurriculumSectionDto {
  return { ...section(id, []), itemType: "Project" };
}

function learningState(overrides: Partial<CourseLearningStateDto> = {}): CourseLearningStateDto {
  return {
    courseId: "course-1",
    enrollmentId: "enrollment-1",
    enrolledAt: "2026-06-18T08:00:00Z",
    summary: {
      totalModules: 0,
      materialsViewed: 0,
      materialsTotal: 0,
      modulesCompleted: 0,
      issuesCompleted: 0,
      issuesTotal: 0,
      totalItems: 0,
      completedItems: 0,
      progressPercent: 0,
    },
    materials: [],
    issues: [],
    passedQuizIds: [],
    lastPosition: null,
    ...overrides,
  };
}

describe("selectProgramPreviewSections", () => {
  it("returns every module in order with sequential numbers (#662 — no 3-module window)", () => {
    const sections = [
      section("m1", [{ id: "a", itemType: "Material" }]),
      section("m2", [{ id: "b", itemType: "Material" }]),
      section("m3", [{ id: "c", itemType: "Issue" }]),
      section("m4", [{ id: "d", itemType: "Material" }]),
      section("m5", [{ id: "e", itemType: "Material" }]),
    ];

    const preview = selectProgramPreviewSections(
      sections,
      learningState({
        lastPosition: {
          entityId: "c",
          entityType: "ISSUE",
          openedAt: "2026-06-18T10:00:00Z",
        },
      }),
    );

    // Full program, starting from module 01 — not a window around the active module.
    expect(preview.map((item) => item.section.id)).toEqual(["m1", "m2", "m3", "m4", "m5"]);
    expect(preview.map((item) => item.sectionNumber)).toEqual([1, 2, 3, 4, 5]);
  });

  it("marks the module containing lastPosition as active", () => {
    const sections = [
      section("m1", [{ id: "a", itemType: "Material" }]),
      section("m2", [{ id: "b", itemType: "Material" }]),
      section("m3", [{ id: "c", itemType: "Issue" }]),
    ];

    const preview = selectProgramPreviewSections(
      sections,
      learningState({
        lastPosition: {
          entityId: "c",
          entityType: "ISSUE",
          openedAt: "2026-06-18T10:00:00Z",
        },
      }),
    );

    expect(preview.map((item) => item.isActive)).toEqual([false, false, true]);
  });

  it("falls back to the first unfinished module when lastPosition is missing", () => {
    const sections = [
      section("m1", [
        { id: "mat-1", itemType: "Material" },
        { id: "issue-1", itemType: "Issue" },
        { id: "quiz-1", itemType: "Quiz" },
      ]),
      section("m2", [{ id: "mat-2", itemType: "Material" }]),
      section("m3", [{ id: "mat-3", itemType: "Material" }]),
    ];

    const preview = selectProgramPreviewSections(
      sections,
      learningState({
        materials: [{ materialId: "mat-1", status: "VIEWED", viewedAt: "2026-06-18T09:00:00Z" }],
        issues: [
          {
            issueId: "issue-1",
            projectId: "project-1",
            status: "COMPLETED",
            startedAt: "2026-06-18T09:00:00Z",
            completedAt: "2026-06-18T09:30:00Z",
            latestSubmission: null,
          },
        ],
        passedQuizIds: ["quiz-1"],
      }),
    );

    // Still the whole program; only the first unfinished module is marked active.
    expect(preview.map((item) => item.section.id)).toEqual(["m1", "m2", "m3"]);
    expect(preview.map((item) => item.isActive)).toEqual([false, true, false]);
  });

  it("returns all modules with no active module when progress is unavailable", () => {
    const preview = selectProgramPreviewSections([
      section("m1", []),
      section("m2", []),
      section("m3", []),
      section("m4", []),
    ]);

    expect(preview.map((item) => item.section.id)).toEqual(["m1", "m2", "m3", "m4"]);
    expect(preview.every((item) => !item.isActive)).toBe(true);
  });

  it("ignores non-module sections and numbers modules by their module order", () => {
    const preview = selectProgramPreviewSections(
      [section("m1", []), project("p1"), section("m2", []), project("p2")],
      learningState(),
    );

    expect(preview.map((item) => item.section.id)).toEqual(["m1", "m2"]);
    expect(preview.map((item) => item.sectionNumber)).toEqual([1, 2]);
  });

  it("returns an empty array when there are no modules", () => {
    expect(selectProgramPreviewSections([project("p1")], learningState())).toEqual([]);
    expect(selectProgramPreviewSections([], learningState())).toEqual([]);
  });
});
