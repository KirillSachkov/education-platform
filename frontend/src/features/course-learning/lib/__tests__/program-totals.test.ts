import { describe, expect, it } from "vitest";
import type { CurriculumCollectionDto, CurriculumSectionDto } from "@/entities/course";
import { computeProgramTotals } from "../program-totals";

function section(id: string, items: Array<{ id: string; itemType: string }>): CurriculumSectionDto {
  return {
    id,
    itemType: "Module",
    title: `Модуль ${id}`,
    description: null,
    detailedDescription: null,
    sortKey: "a",
    isOptional: false,
    items: items.map((i, position) => ({
      id: i.id,
      itemType: i.itemType,
      title: `Item ${i.id}`,
      sortKey: "a",
      isOptional: false,
      position,
      accessType: "PUBLIC",
      viewPriority: null,
      materialKind: null,
      durationSeconds: null,
      coverUrl: null,
    })),
  };
}

function collection(id: string, materialIds: string[]): CurriculumCollectionDto {
  return {
    id,
    title: `Подборка ${id}`,
    description: null,
    accessType: "ENROLLED",
    itemsCount: materialIds.length,
    materialIds,
    coverUrl: null,
  };
}

describe("computeProgramTotals", () => {
  // Reproducer #522: курс без модулей, но с подборками — счётчики не должны быть 0.
  it("считает материалы подборок при пустой программе", () => {
    const totals = computeProgramTotals(
      [],
      [collection("c1", ["m1", "m2", "m3"]), collection("c2", ["m4"])],
    );

    expect(totals.totalMaterials).toBe(4);
    expect(totals.totalItems).toBe(4);
    expect(totals.materialIds).toEqual(new Set(["m1", "m2", "m3", "m4"]));
  });

  it("суммирует элементы модулей и материалы подборок", () => {
    const totals = computeProgramTotals(
      [
        section("s1", [
          { id: "m1", itemType: "Material" },
          { id: "i1", itemType: "Issue" },
          { id: "q1", itemType: "Quiz" },
        ]),
      ],
      [collection("c1", ["m2", "m3"])],
    );

    expect(totals.totalMaterials).toBe(3);
    expect(totals.totalIssues).toBe(1);
    expect(totals.totalQuizzes).toBe(1);
    expect(totals.totalItems).toBe(5);
    expect(totals.issueIds).toEqual(new Set(["i1"]));
    expect(totals.quizIds).toEqual(new Set(["q1"]));
  });

  it("дедуплицирует материал, который есть и в модуле, и в подборке", () => {
    const totals = computeProgramTotals(
      [section("s1", [{ id: "m1", itemType: "Material" }])],
      [collection("c1", ["m1", "m2"])],
    );

    expect(totals.totalMaterials).toBe(2);
    expect(totals.totalItems).toBe(2);
    expect(totals.materialIds).toEqual(new Set(["m1", "m2"]));
  });

  it("возвращает нули без модулей и подборок", () => {
    const totals = computeProgramTotals([], []);

    expect(totals.totalItems).toBe(0);
    expect(totals.totalMaterials).toBe(0);
    expect(totals.materialIds.size).toBe(0);
  });
});
