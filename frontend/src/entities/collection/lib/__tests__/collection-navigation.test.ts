import { describe, expect, it } from "vitest";
import { getAdjacentCollectionMaterials, getCollectionNavigationItems } from "../collection-navigation";
import type { CollectionDetailDto, CollectionItemDto } from "../../types";
import type { MaterialSummaryDto } from "@/entities/material";

function materialSummary(id: string, title: string): MaterialSummaryDto {
  return {
    id,
    authorId: "a1",
    title,
    preview: null,
    kind: "ARTICLE",
    status: "PUBLISHED",
    accessType: "PUBLIC",
    createdAt: "",
    updatedAt: "",
    publishedAt: null,
  };
}

type ItemSpec =
  | { materialId: string; title: string; isAccessible?: boolean }
  | { quizId: string; title: string };

function toItem(spec: ItemSpec, sectionId: string, idx: number): CollectionItemDto {
  if ("quizId" in spec) {
    return {
      id: `${sectionId}-item-${idx}`,
      referenceId: spec.quizId,
      itemType: "QUIZ",
      material: null,
      quizTitle: spec.title,
      questionsCount: 5,
      isAccessible: true,
      lockReason: null,
    };
  }
  return {
    id: `${sectionId}-item-${idx}`,
    referenceId: spec.materialId,
    itemType: "MATERIAL",
    material: materialSummary(spec.materialId, spec.title),
    quizTitle: null,
    questionsCount: null,
    isAccessible: spec.isAccessible ?? true,
    lockReason: spec.isAccessible === false ? "plan_required" : null,
  };
}

function collection(
  sections: Array<{
    id: string;
    items: ItemSpec[];
  }>,
): CollectionDetailDto {
  return {
    id: "col1",
    authorId: "a1",
    title: "Collection",
    description: null,
    coverImageId: null,
    coverImageUrl: null,
    courseId: null,
    courseTitle: null,
    courseSlug: null,
    status: "PUBLISHED",
    accessType: "PUBLIC",
    isAccessible: true,
    lockReason: null,
    createdAt: "",
    updatedAt: "",
    sections: sections.map((s) => ({
      id: s.id,
      title: null,
      description: null,
      items: s.items.map((i, idx) => toItem(i, s.id, idx)),
    })),
  };
}

describe("getAdjacentCollectionMaterials", () => {
  it("walks across section boundaries in flat render order", () => {
    const c = collection([
      {
        id: "s1",
        items: [
          { materialId: "m1", title: "M1" },
          { materialId: "m2", title: "M2" },
        ],
      },
      { id: "s2", items: [{ materialId: "m3", title: "M3" }] },
    ]);
    const nav = getAdjacentCollectionMaterials(c, "m2");
    expect(nav.previousItem?.materialId).toBe("m1");
    expect(nav.nextItem?.materialId).toBe("m3");
    expect(nav.nextItem?.title).toBe("M3");
  });

  it("returns null neighbours at the first/last item", () => {
    const c = collection([
      {
        id: "s1",
        items: [
          { materialId: "m1", title: "M1" },
          { materialId: "m2", title: "M2" },
        ],
      },
    ]);
    expect(getAdjacentCollectionMaterials(c, "m1").previousItem).toBeNull();
    expect(getAdjacentCollectionMaterials(c, "m2").nextItem).toBeNull();
  });

  it("returns nothing when the material is not in the collection", () => {
    const c = collection([{ id: "s1", items: [{ materialId: "m1", title: "M1" }] }]);
    const nav = getAdjacentCollectionMaterials(c, "missing");
    expect(nav.currentItem).toBeNull();
    expect(nav.previousItem).toBeNull();
    expect(nav.nextItem).toBeNull();
  });

  it("returns nothing for a missing collection", () => {
    const nav = getAdjacentCollectionMaterials(undefined, "m1");
    expect(nav.items).toHaveLength(0);
    expect(nav.currentItem).toBeNull();
  });

  it("does not skip locked items — they stay linkable neighbours", () => {
    const c = collection([
      {
        id: "s1",
        items: [
          { materialId: "m1", title: "M1" },
          { materialId: "m2", title: "Locked", isAccessible: false },
          { materialId: "m3", title: "M3" },
        ],
      },
    ]);
    const nav = getAdjacentCollectionMaterials(c, "m1");
    expect(nav.nextItem?.materialId).toBe("m2");
  });

  it("dedupes a material repeated in another section by first occurrence", () => {
    const c = collection([
      {
        id: "s1",
        items: [
          { materialId: "m1", title: "M1" },
          { materialId: "m2", title: "M2" },
        ],
      },
      {
        id: "s2",
        items: [
          { materialId: "m1", title: "M1 again" },
          { materialId: "m3", title: "M3" },
        ],
      },
    ]);
    expect(getCollectionNavigationItems(c)).toHaveLength(3);
    const nav = getAdjacentCollectionMaterials(c, "m1");
    expect(nav.previousItem).toBeNull();
    expect(nav.nextItem?.materialId).toBe("m2");
  });

  it("skips QUIZ items (#491) — prev/next ходит только по материалам", () => {
    const c = collection([
      {
        id: "s1",
        items: [
          { materialId: "m1", title: "M1" },
          { quizId: "q1", title: "Квиз 1" },
          { materialId: "m2", title: "M2" },
        ],
      },
    ]);
    expect(getCollectionNavigationItems(c).map((i) => i.materialId)).toEqual(["m1", "m2"]);
    const nav = getAdjacentCollectionMaterials(c, "m2");
    expect(nav.previousItem?.materialId).toBe("m1");
    expect(nav.nextItem).toBeNull();
    expect(getAdjacentCollectionMaterials(c, "q1").currentItem).toBeNull();
  });
});
